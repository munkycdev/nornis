using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The nav badge counts are now computed in SQL rather than by loading every source and grouping
/// in memory. That moved a security predicate into the database, so these run the real query
/// against a relational provider: an in-memory fake could agree with the C# rule while the
/// generated SQL leaked.
///
/// The seeded world deliberately contains one source of every awkward kind, so each role's
/// expected counts differ from every other role's. A predicate that silently widened would show
/// up as a role seeing a status it should not.
/// </summary>
[TestFixture]
public class SourceActivityCountTests : IntegrationTestBase
{
    private SourceRepository _repository = null!;
    private Guid _worldId;
    private Guid _gmId;
    private Guid _playerId;
    private Guid _otherPlayerId;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _otherPlayerId = Guid.NewGuid();

        Context.Sources.RemoveRange(Context.Sources);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.AddRange(
            MakeUser(_gmId, "kelda"), MakeUser(_playerId, "tavrin"), MakeUser(_otherPlayerId, "sable"));
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        // Ready ------------------------------------------------------------------
        Seed(VisibilityScope.PartyVisible, _otherPlayerId, SourceProcessingStatus.Ready);   // everyone
        Seed(VisibilityScope.Private, _playerId, SourceProcessingStatus.Ready);             // GM + owner
        Seed(VisibilityScope.Private, _otherPlayerId, SourceProcessingStatus.Ready);        // GM + other owner
        Seed(VisibilityScope.GMOnly, _gmId, SourceProcessingStatus.Ready);                  // GM only

        // Queued -----------------------------------------------------------------
        Seed(VisibilityScope.PartyVisible, _gmId, SourceProcessingStatus.Queued);           // everyone

        // Processing -------------------------------------------------------------
        Seed(VisibilityScope.GMOnly, _gmId, SourceProcessingStatus.Processing);             // GM only

        // Failed -----------------------------------------------------------------
        Seed(VisibilityScope.Private, _playerId, SourceProcessingStatus.Failed);            // GM + owner

        // Draft: never counted by the badge, but seeded so a predicate that forgot the draft
        // gate would show up as an unexpected status key.
        Seed(VisibilityScope.PartyVisible, _otherPlayerId, SourceProcessingStatus.Draft);

        await Context.SaveChangesAsync();
        _repository = new SourceRepository(Context);
    }

    private static User MakeUser(Guid id, string name) => new()
    {
        Id = id,
        Auth0SubjectId = $"auth0|{id:N}",
        Username = name,
        Email = $"{name}@example.com",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    private void Seed(VisibilityScope visibility, Guid createdBy, SourceProcessingStatus status) =>
        Context.Sources.Add(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = $"{visibility}/{status}",
            Body = "body text",
            Visibility = visibility,
            ProcessingStatus = status,
            CreatedByUserId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        });

    private async Task<IReadOnlyDictionary<SourceProcessingStatus, int>> CountAsync(Guid userId, WorldRole role) =>
        await _repository.CountByStatusAsync(_worldId, userId, role);

    [Test]
    public async Task Gm_CountsEverySourceInTheWorld()
    {
        var counts = await CountAsync(_gmId, WorldRole.GM);

        Assert.Multiple(() =>
        {
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(4));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Queued), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Processing), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Failed), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Draft), Is.EqualTo(1),
                "the GM sees other people's drafts");
        });
    }

    [Test]
    public async Task PlayerOwner_CountsPartyVisiblePlusTheirOwnPrivate()
    {
        var counts = await CountAsync(_playerId, WorldRole.Player);

        Assert.Multiple(() =>
        {
            // PartyVisible/Ready + their own Private/Ready. NOT the other player's Private,
            // NOT the GM-only one.
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(2));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Queued), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Processing), Is.Zero,
                "the only Processing source is GM-only");
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Failed), Is.EqualTo(1),
                "their own Private failure is theirs to see");
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Draft), Is.Zero,
                "the only draft belongs to someone else");
        });
    }

    [Test]

    [Category("Authorization")]
    public async Task PlayerNotTheOwner_CountsPartyVisibleOnly()
    {
        var counts = await CountAsync(_otherPlayerId, WorldRole.Player);

        Assert.Multiple(() =>
        {
            // PartyVisible/Ready + their own Private/Ready.
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(2));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Failed), Is.Zero,
                "the failed source is another player's Private note");
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Processing), Is.Zero);
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Draft), Is.EqualTo(1),
                "their own draft is theirs to see");
        });
    }

    [Test]

    [Category("Authorization")]
    public async Task Observer_WhoAuthoredNothing_CountsPartyVisibleNonDraftOnly()
    {
        // A stranger's identity: owns none of the seeded rows, so nothing can reach them by
        // ownership and only the scope gate applies.
        var strangerId = Guid.NewGuid();

        var counts = await CountAsync(strangerId, WorldRole.Observer);

        Assert.Multiple(() =>
        {
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Queued), Is.EqualTo(1));
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Processing), Is.Zero);
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Failed), Is.Zero);
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Draft), Is.Zero,
                "an Observer never sees an unfinished note");
        });
    }

    [Test]
    public async Task OwnPrivateSource_RemainsVisibleRegardlessOfRole()
    {
        // Documents the rule as written rather than as one might assume: the Private gate is
        // "GM or author", with no further role test. An Observer who authored a Private source
        // still sees it. Pinned because the obvious "Observers see only PartyVisible" reading is
        // wrong, and a future tightening should be a deliberate decision, not a silent one.
        var counts = await CountAsync(_otherPlayerId, WorldRole.Observer);

        Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(2),
            "the PartyVisible one plus this user's own Private Ready source");
    }

    [Test]
    public async Task AnonymousReader_MatchesNothingByOwnership()
    {
        // The public world page reads as Observer with an empty id. Note the schema makes a truly
        // unattributable row impossible — CreatedByUserId is a real foreign key to Users — so the
        // empty-id guard is defence in depth rather than a live case. What it must guarantee is
        // that an empty id never behaves as a wildcard owner.
        var counts = await CountAsync(Guid.Empty, WorldRole.Observer);

        Assert.Multiple(() =>
        {
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(1),
                "only the PartyVisible Ready source");
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Failed), Is.Zero);
            Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Draft), Is.Zero);
        });
    }

    [Test]
    public async Task OtherWorlds_AreNeverCounted()
    {
        var otherWorldId = Guid.NewGuid();
        Context.Worlds.Add(new World
        {
            Id = otherWorldId,
            Name = "Elsewhere",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        Context.Sources.Add(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = otherWorldId,
            Type = SourceType.SessionNote,
            Title = "Another world's note",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Ready,
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        var counts = await CountAsync(_gmId, WorldRole.GM);

        Assert.That(counts.GetValueOrDefault(SourceProcessingStatus.Ready), Is.EqualTo(4));
    }

    [Test]
    public async Task StatusesWithNoRows_AreAbsentRatherThanZero()
    {
        var counts = await CountAsync(_otherPlayerId, WorldRole.Observer);

        Assert.That(counts.ContainsKey(SourceProcessingStatus.Failed), Is.False);
    }
}
