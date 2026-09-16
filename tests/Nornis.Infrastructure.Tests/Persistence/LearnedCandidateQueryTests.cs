using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The two queries What you learned makes on every nav-badge poll, against a real relational
/// provider: the candidate projection (marker cut and sort share one date expression, newest
/// first, no body loaded) and the one-query batch lookup across many sources.
/// </summary>
[TestFixture]
public class LearnedCandidateQueryTests : IntegrationTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    private World _world = null!;
    private User _gm = null!;

    [SetUp]
    public void SetUp()
    {
        var tag = Guid.NewGuid().ToString("N");
        _gm = new User
        {
            Id = Guid.NewGuid(),
            Auth0SubjectId = $"auth0|{tag}",
            Username = $"gm-{tag}",
            Email = $"{tag}@example.com",
            CreatedAt = Now,
            UpdatedAt = Now,
            RowVersion = []
        };
        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Vespergale",
            CreatedAt = Now,
            UpdatedAt = Now,
            CreatedByUserId = _gm.Id,
            RowVersion = []
        };
        Context.Users.Add(_gm);
        Context.Worlds.Add(_world);
        Context.SaveChanges();
    }

    private Source SeedSource(
        string title, DateTimeOffset createdAt, DateTimeOffset? occurredAt,
        SourceType type = SourceType.SessionNote, Guid? worldId = null, string? revealNote = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _world.Id,
            Type = type,
            Title = title,
            Body = new string('x', 4000),
            OccurredAt = occurredAt,
            CreatedAt = createdAt,
            CreatedByUserId = _gm.Id,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            RevealNote = revealNote
        };
        Context.Sources.Add(source);
        Context.SaveChanges();
        return source;
    }

    [Test]
    public async Task Candidates_AreNewestFirst_ByWhenTheyHappened_ElseWhenWritten()
    {
        // Written today about last week, written yesterday about nothing in particular, and a
        // reveal written today: the session about last week sorts by last week.
        var lastWeek = SeedSource("Session about last week", Now, Now.AddDays(-7));
        var yesterday = SeedSource("Note", Now.AddDays(-1), null);
        var reveal = SeedSource("Reveal", Now, null, SourceType.Reveal, revealNote: "You have learned.");
        SeedSource("Elsewhere", Now, null, worldId: SeedOtherWorld().Id);
        var sut = new SourceRepository(Context);

        var candidates = await sut.ListLearnedCandidatesAsync(_world.Id, since: null);

        Assert.That(candidates.Select(c => c.Id), Is.EqualTo([reveal.Id, yesterday.Id, lastWeek.Id]).AsCollection);
        Assert.That(candidates[0].RevealNote, Is.EqualTo("You have learned."));
        Assert.That(candidates[0].Type, Is.EqualTo(SourceType.Reveal));
        Assert.That(candidates[2].OccurredAt, Is.EqualTo(lastWeek.OccurredAt),
            "the projected date is when it happened where that is recorded");
        Assert.That(candidates[1].OccurredAt, Is.EqualTo(yesterday.CreatedAt),
            "and when it was written otherwise");
    }

    [Test]
    public async Task Candidates_BehindTheMarker_AreCutByTheSameDateTheyAreSortedBy()
    {
        // Written after the marker but about a session before it: behind the marker, because the
        // page would also sort it there. A source cannot be new by one date and old by the other.
        SeedSource("Late write-up of an old session", Now, Now.AddDays(-7));
        var fresh = SeedSource("Fresh", Now.AddDays(-1), null);
        var sut = new SourceRepository(Context);

        var candidates = await sut.ListLearnedCandidatesAsync(_world.Id, since: Now.AddDays(-3));

        Assert.That(candidates.Select(c => c.Id), Is.EqualTo([fresh.Id]).AsCollection);
    }

    [Test]
    public async Task BatchesBySourceIds_ReturnsEveryBatchOfEveryListedSource_InOneCall()
    {
        var a = SeedSource("A", Now, null);
        var b = SeedSource("B", Now, null);
        var unlisted = SeedSource("C", Now, null);
        Context.ReviewBatches.AddRange(
            MakeBatch(a.Id, kind: null),
            MakeBatch(a.Id, kind: ReviewBatchKinds.Reveal),
            MakeBatch(b.Id, kind: null),
            MakeBatch(unlisted.Id, kind: null));
        Context.SaveChanges();
        var sut = new ReviewBatchRepository(Context);

        var batches = await sut.ListBySourceIdsAsync([a.Id, b.Id]);

        Assert.That(batches.Count(x => x.SourceId == a.Id), Is.EqualTo(2));
        Assert.That(batches.Count(x => x.SourceId == b.Id), Is.EqualTo(1));
        Assert.That(batches.Any(x => x.SourceId == unlisted.Id), Is.False);
        Assert.That(await sut.ListBySourceIdsAsync([]), Is.Empty);
    }

    private ReviewBatch MakeBatch(Guid sourceId, string? kind) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = _world.Id,
        SourceId = sourceId,
        Kind = kind,
        Status = ReviewBatchStatus.Completed,
        CreatedAt = Now,
        CompletedAt = Now
    };

    private World SeedOtherWorld()
    {
        var other = new World
        {
            Id = Guid.NewGuid(),
            Name = "Elsewhere",
            CreatedAt = Now,
            UpdatedAt = Now,
            CreatedByUserId = _gm.Id,
            RowVersion = []
        };
        Context.Worlds.Add(other);
        Context.SaveChanges();
        return other;
    }
}
