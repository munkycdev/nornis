using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// ListByEquivalentNameAsync backs review name-resolution, which is Player-reachable. Its
/// visibility and archived predicates are expressed inline so EF can translate them to SQL,
/// which means they are NOT the same code as VisibilityFilter.CanSee that the in-memory test
/// fake uses. These tests run the real query against SQLite so the two cannot drift apart.
/// </summary>
[TestFixture]
public class ArtifactNameLookupVisibilityTests : IntegrationTestBase
{
    private Guid _worldId;
    private Guid _playerId;
    private Guid _otherPlayerId;
    private ArtifactRepository _repository = null!;

    private const string Name = "Captain Voss";

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _otherPlayerId = Guid.NewGuid();

        Context.Artifacts.RemoveRange(Context.Artifacts);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        // Artifact.CreatedByUserId is a real FK, so the owners have to exist as User rows.
        var gmId = Guid.NewGuid();
        Context.Users.AddRange(
            MakeUser(gmId, "Kelda"),
            MakeUser(_playerId, "Tavrin"),
            MakeUser(_otherPlayerId, "Sable"));
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor Investigation",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = gmId,
            RowVersion = []
        });
        await Context.SaveChangesAsync();

        _repository = new ArtifactRepository(Context);
    }

    [Test]
    public async Task GmOnlyArtifact_IsInvisibleToAPlayer()
    {
        await SeedAsync(VisibilityScope.GMOnly);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches, Is.Empty, "the SQL predicate must drop GM-only rows for a Player");
    }

    [Test]
    public async Task AnotherUsersPrivateArtifact_IsInvisibleToAPlayer()
    {
        await SeedAsync(VisibilityScope.Private, createdByUserId: _otherPlayerId);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches, Is.Empty, "Private rows are gated on ownership in SQL, not just in memory");
    }

    [Test]
    public async Task UnattributedPrivateArtifact_IsInvisibleToAPlayer()
    {
        // NULL CreatedByUserId — a legacy or GM-authored Private row. The SQL comparison
        // a.CreatedByUserId == owner is NULL-vs-value, which must not match. Fail closed.
        await SeedAsync(VisibilityScope.Private, createdByUserId: null);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches, Is.Empty, "an unattributable Private row must fail closed for a Player");
    }

    [Test]
    public async Task ArchivedArtifact_IsInvisibleEvenToAnUnrestrictedReader()
    {
        await SeedAsync(VisibilityScope.PartyVisible, status: ArtifactStatus.Archived);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, VisibilityFilter.All);

        Assert.That(matches, Is.Empty, "archived merge leftovers must not resolve by name for anyone");
    }

    [Test]
    public async Task PartyVisibleArtifact_IsVisibleToAPlayer()
    {
        var expected = await SeedAsync(VisibilityScope.PartyVisible);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public async Task OwnPrivateArtifact_IsVisibleToItsOwner()
    {
        var expected = await SeedAsync(VisibilityScope.Private, createdByUserId: _playerId);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { expected }),
            "ownership must be honoured in SQL, or Players lose their own Private artifacts");
    }

    [Test]
    public async Task GmOnlyArtifact_IsVisibleToAGm()
    {
        var expected = await SeedAsync(VisibilityScope.GMOnly);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, VisibilityFilter.All);

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public async Task HiddenDuplicate_DoesNotJoinThePlayersMatchSet()
    {
        // The ambiguity branch upstream counts these rows, so a hidden duplicate leaking in
        // here would turn a clean resolution into a "multiple artifacts named X" disclosure.
        var visible = await SeedAsync(VisibilityScope.PartyVisible);
        await SeedAsync(VisibilityScope.GMOnly);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { visible }));
    }

    [Test]
    public async Task MatchIsCaseInsensitive()
    {
        var expected = await SeedAsync(VisibilityScope.PartyVisible);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, "captain voss", PlayerFilter());

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { expected }),
            "adding the visibility predicate must not have disturbed the name comparison");
    }

    [Test]
    public async Task MatchCollapsesInternalWhitespace()
    {
        // Name resolution must use the same ArtifactNameKey equivalence as apply-time dedup.
        // While it did not, a create proposing "Captain  Voss" bound to canon's "Captain Voss"
        // and then every fact in the batch referencing the sloppy spelling failed forever.
        var expected = await SeedAsync(VisibilityScope.PartyVisible);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, "Captain  Voss", PlayerFilter());

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public async Task MatchCollapsesWhitespaceStoredInCanonToo()
    {
        // The mirror case: the sloppy spelling is the one already in the database.
        var expected = await SeedAsync(VisibilityScope.PartyVisible, name: "Captain  Voss");

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches.Select(a => a.Id), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public async Task WhitespaceVariantsOfTheSameNameAreAllAmbiguous()
    {
        // Counting ambiguity over the equivalence set rather than the exact-match subset is
        // what stops a duplicate hiding behind a stray double space.
        await SeedAsync(VisibilityScope.PartyVisible);
        await SeedAsync(VisibilityScope.PartyVisible, name: "Captain  Voss");

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches, Has.Count.EqualTo(2), "the caller must be able to report ambiguity");
    }

    [Test]
    public async Task ArticlesAreNotStripped()
    {
        await SeedAsync(VisibilityScope.PartyVisible);

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, "The Captain Voss", PlayerFilter());

        Assert.That(matches, Is.Empty);
    }

    [Test]
    public async Task DifferentNamesInTheSameWorldDoNotMatch()
    {
        // The name predicate moved out of SQL; prove it still actually discriminates.
        await SeedAsync(VisibilityScope.PartyVisible, name: "Black Harbor");

        var matches = await _repository.ListByEquivalentNameAsync(_worldId, Name, PlayerFilter());

        Assert.That(matches, Is.Empty);
    }

    private VisibilityFilter PlayerFilter() => VisibilityFilter.ForRole(WorldRole.Player, _playerId);

    private static User MakeUser(Guid id, string username) => new()
    {
        Id = id,
        Auth0SubjectId = $"auth0|{id:N}",
        Username = username,
        Email = $"{username.ToLowerInvariant()}@blackharbor.test",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        RowVersion = []
    };

    private async Task<Guid> SeedAsync(
        VisibilityScope visibility,
        Guid? createdByUserId = null,
        ArtifactStatus status = ArtifactStatus.Active,
        string? name = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = name ?? Name,
            Visibility = visibility,
            Status = status,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };

        Context.Artifacts.Add(artifact);
        await Context.SaveChangesAsync();
        return artifact.Id;
    }
}
