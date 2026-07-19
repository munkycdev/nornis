using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ArtifactServiceSearchTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private ArtifactService _service = null!;

    // World: "Black Harbor Investigation"
    private Guid _worldId;
    private Guid _otherWorldId;

    // Users
    private Guid _keldaUserId;   // GM
    private Guid _tavrinUserId;  // Player

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _service = new ArtifactService(
            _artifactRepo,
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(),
            new InMemorySourceRepository(),
            new InMemoryCharacterRepository(),
            new InMemoryWorldMemberRepository());

        _worldId = Guid.NewGuid();
        _otherWorldId = Guid.NewGuid();
        _keldaUserId = Guid.NewGuid();
        _tavrinUserId = Guid.NewGuid();
    }

    [Test]
    public async Task SearchAsync_OrdersByRelevanceNotRecency()
    {
        // Seeded oldest-first so a recency-ordered result would invert the expected order.
        _artifactRepo.Seed(
            MakeArtifact("Voss", VisibilityScope.PartyVisible, updatedAt: Days(-9)),
            MakeArtifact("Vossberg Keep", VisibilityScope.PartyVisible, updatedAt: Days(-6)),
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible, updatedAt: Days(-3)),
            MakeArtifact("Black Harbor", VisibilityScope.PartyVisible, summary: "Voss keeps a ledger here.", updatedAt: Days(-1)));

        var result = await _service.SearchAsync(Query("voss"), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[]
        {
            "Voss",           // exact name
            "Vossberg Keep",  // name prefix
            "Captain Voss",   // whole word inside name
            "Black Harbor",   // summary only
        }));
    }

    [Test]
    public async Task SearchAsync_TiedRelevance_PrefersShorterNameThenMoreRecent()
    {
        _artifactRepo.Seed(
            MakeArtifact("Voss Harbor Authority", VisibilityScope.PartyVisible, updatedAt: Days(-1)),
            MakeArtifact("Vossberg", VisibilityScope.PartyVisible, updatedAt: Days(-8)));

        var result = await _service.SearchAsync(Query("voss"), CancellationToken.None);

        // Both are prefix matches; the shorter, more specific name wins despite being staler.
        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[]
        {
            "Vossberg",
            "Voss Harbor Authority",
        }));
    }

    [Test]
    public async Task SearchAsync_ExcludesNonMatches()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("The Sunken Wharf", VisibilityScope.PartyVisible));

        var result = await _service.SearchAsync(Query("voss"), CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[] { "Captain Voss" }));
    }

    [Test]
    public async Task SearchAsync_ExcludesArchivedArtifacts()
    {
        // Archived artifacts are merge leftovers — a search hit would send the user to a dead end.
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Capt. Voss", VisibilityScope.PartyVisible, status: ArtifactStatus.Archived));

        var result = await _service.SearchAsync(Query("voss"), CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[] { "Captain Voss" }));
    }

    [Test]
    public async Task SearchAsync_ExcludesOtherWorlds()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Voss the Elder", VisibilityScope.PartyVisible, worldId: _otherWorldId));

        var result = await _service.SearchAsync(Query("voss"), CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[] { "Captain Voss" }));
    }

    [Test]
    public async Task SearchAsync_Gm_SeesAllVisibilityScopes()
    {
        _artifactRepo.Seed(
            MakeArtifact("Voss the Captain", VisibilityScope.PartyVisible),
            MakeArtifact("Voss's Hidden Ledger", VisibilityScope.GMOnly),
            MakeArtifact("Voss Private Note", VisibilityScope.Private, createdByUserId: _tavrinUserId));

        var result = await _service.SearchAsync(Query("voss"), CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SearchAsync_Player_DoesNotLeakGmOnlyOrOthersPrivateArtifacts()
    {
        _artifactRepo.Seed(
            MakeArtifact("Voss the Captain", VisibilityScope.PartyVisible),
            MakeArtifact("Voss's Hidden Ledger", VisibilityScope.GMOnly),
            MakeArtifact("Voss Note by Tavrin", VisibilityScope.Private, createdByUserId: _tavrinUserId),
            MakeArtifact("Voss Note by Kelda", VisibilityScope.Private, createdByUserId: _keldaUserId));

        var result = await _service.SearchAsync(
            Query("voss", _tavrinUserId, WorldRole.Player), CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Name), Is.EquivalentTo(new[]
        {
            "Voss the Captain",
            "Voss Note by Tavrin",
        }));
    }

    [Test]
    public async Task SearchAsync_Observer_SeesOnlyPartyVisibleArtifacts()
    {
        _artifactRepo.Seed(
            MakeArtifact("Voss the Captain", VisibilityScope.PartyVisible),
            MakeArtifact("Voss's Hidden Ledger", VisibilityScope.GMOnly),
            MakeArtifact("Voss Private Note", VisibilityScope.Private, createdByUserId: _tavrinUserId));

        var result = await _service.SearchAsync(
            Query("voss", _tavrinUserId, WorldRole.Observer), CancellationToken.None);

        Assert.That(result.Value!.Select(a => a.Name), Is.EqualTo(new[] { "Voss the Captain" }));
    }

    [TestCase("")]
    [TestCase("   ")]
    public async Task SearchAsync_BlankTerm_ReturnsEmptyRatherThanEverything(string term)
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Black Harbor", VisibilityScope.PartyVisible));

        var result = await _service.SearchAsync(Query(term), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Is.Empty);
    }

    [Test]
    public async Task SearchAsync_AppliesLimit()
    {
        _artifactRepo.Seed(Enumerable.Range(0, 12)
            .Select(i => MakeArtifact($"Voss Sighting {i}", VisibilityScope.PartyVisible)));

        var result = await _service.SearchAsync(Query("voss") with { Limit = 5 }, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task SearchAsync_ClampsLimitToAtLeastOne()
    {
        _artifactRepo.Seed(
            MakeArtifact("Captain Voss", VisibilityScope.PartyVisible),
            MakeArtifact("Voss Harbor", VisibilityScope.PartyVisible));

        var result = await _service.SearchAsync(Query("voss") with { Limit = 0 }, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SearchAsync_ClampsLimitToUpperBound()
    {
        _artifactRepo.Seed(Enumerable.Range(0, 60)
            .Select(i => MakeArtifact($"Voss Sighting {i}", VisibilityScope.PartyVisible)));

        var result = await _service.SearchAsync(Query("voss") with { Limit = 500 }, CancellationToken.None);

        Assert.That(result.Value!, Has.Count.EqualTo(50));
    }

    private ArtifactSearchQuery Query(string term, Guid? userId = null, WorldRole role = WorldRole.GM) =>
        new(_worldId, userId ?? _keldaUserId, role, term);

    private static DateTimeOffset Days(int offset) => DateTimeOffset.UtcNow.AddDays(offset);

    private Artifact MakeArtifact(
        string name,
        VisibilityScope visibility,
        string? summary = null,
        ArtifactType type = ArtifactType.Character,
        ArtifactStatus status = ArtifactStatus.Active,
        Guid? worldId = null,
        Guid? createdByUserId = null,
        DateTimeOffset? updatedAt = null)
    {
        var now = updatedAt ?? DateTimeOffset.UtcNow;
        return new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _worldId,
            CreatedByUserId = createdByUserId,
            Type = type,
            Name = name,
            Summary = summary,
            Visibility = visibility,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
