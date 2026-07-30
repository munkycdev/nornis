using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The nav badge's "pending review" count. It decides what a Player is told is waiting for them,
/// so it is a per-role scope decision — and review scope is deliberately STRICTER than source
/// visibility: a Player may read a party-visible source they did not write, but may only review
/// proposals on sources they authored.
///
/// These run the real query against a relational provider and assert it agrees with
/// <c>ListReviewQueueAsync</c>, which is the thing the badge is summarising. The two disagreeing
/// would show up as a badge promising work the review page does not list.
/// </summary>
[TestFixture]
public class ReviewerProposalCountTests : IntegrationTestBase
{
    private ReviewProposalRepository _repository = null!;
    private Guid _worldId;
    private Guid _gmId;
    private Guid _playerId;
    private Guid _strangerId;
    private Guid _gmSourceId;
    private Guid _playerSourceId;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _gmId = Guid.NewGuid();
        _playerId = Guid.NewGuid();
        _strangerId = Guid.NewGuid();

        Context.ReviewProposals.RemoveRange(Context.ReviewProposals);
        Context.ReviewBatches.RemoveRange(Context.ReviewBatches);
        Context.Sources.RemoveRange(Context.Sources);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        Context.Users.AddRange(MakeUser(_gmId, "kelda"), MakeUser(_playerId, "tavrin"), MakeUser(_strangerId, "sable"));
        Context.Worlds.Add(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();

        _gmSourceId = SeedSource(_gmId);
        _playerSourceId = SeedSource(_playerId);
        await Context.SaveChangesAsync();

        // 3 open proposals on the GM's source, 2 on the player's, plus one already-decided.
        SeedBatchWithProposals(_gmSourceId, open: 3, decided: 1);
        SeedBatchWithProposals(_playerSourceId, open: 2, decided: 0);
        await Context.SaveChangesAsync();

        _repository = new ReviewProposalRepository(Context);
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

    private Guid SeedSource(Guid authorId)
    {
        var id = Guid.NewGuid();
        Context.Sources.Add(new Source
        {
            Id = id,
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = $"Note by {authorId:N}",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = authorId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        return id;
    }

    private void SeedBatchWithProposals(Guid sourceId, int open, int decided)
    {
        var batchId = Guid.NewGuid();
        Context.ReviewBatches.Add(new ReviewBatch
        {
            Id = batchId,
            WorldId = _worldId,
            SourceId = sourceId,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        for (var i = 0; i < open + decided; i++)
        {
            Context.ReviewProposals.Add(new ReviewProposal
            {
                Id = Guid.NewGuid(),
                ReviewBatchId = batchId,
                ChangeType = ReviewChangeType.CreateArtifact,
                TargetType = ReviewTargetType.Artifact,
                ProposedValueJson = """{"name":"X","type":"Character"}""",
                Rationale = "because",
                // Edited counts as open, same as Pending.
                Status = i < open
                    ? (i % 2 == 0 ? ReviewProposalStatus.Pending : ReviewProposalStatus.Edited)
                    : ReviewProposalStatus.Accepted,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    /// <summary>The count must match what the queue would actually hand back.</summary>
    private async Task AssertAgreesWithQueueAsync(Guid userId, WorldRole role, int expected)
    {
        var (count, _) = await _repository.CountOpenForReviewerAsync(_worldId, userId, role, 200);

        var allowedSourceIds = role switch
        {
            WorldRole.GM => new List<Guid> { _gmSourceId, _playerSourceId },
            WorldRole.Player => Context.Sources
                .Where(s => s.WorldId == _worldId && s.CreatedByUserId == userId)
                .Select(s => s.Id).ToList(),
            _ => [],
        };

        var queueCount = allowedSourceIds.Count == 0
            ? 0
            : (await _repository.ListReviewQueueAsync(_worldId, allowedSourceIds, null, 200)).Proposals.Count;

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(expected));
            Assert.That(count, Is.EqualTo(queueCount), "the badge and the review queue must agree");
        });
    }

    [Test]
    public Task Gm_CountsEveryOpenProposalInTheWorld() =>
        AssertAgreesWithQueueAsync(_gmId, WorldRole.GM, 5);

    [Test]
    public Task Player_CountsOnlyProposalsOnSourcesTheyAuthored() =>
        AssertAgreesWithQueueAsync(_playerId, WorldRole.Player, 2);

    [Test]
    public Task PlayerWhoAuthoredNothing_CountsZero() =>
        AssertAgreesWithQueueAsync(_strangerId, WorldRole.Player, 0);

    [Test]
    public async Task Observer_CountsZeroWithoutQuerying()
    {
        var (count, hasMore) = await _repository.CountOpenForReviewerAsync(_worldId, _gmId, WorldRole.Observer, 200);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.Zero);
            Assert.That(hasMore, Is.False);
        });
    }

    [Test]
    public async Task DecidedProposals_AreNotCounted()
    {
        // The GM's batch has one Accepted proposal alongside three open ones.
        var (count, _) = await _repository.CountOpenForReviewerAsync(_worldId, _gmId, WorldRole.GM, 200);

        Assert.That(count, Is.EqualTo(5), "6 proposals exist; one is already decided");
    }

    [Test]
    public async Task AtTheCap_ReportsTheCapAndHasMore()
    {
        // Mirrors the queue's limit + 1 probe, so the badge says "3+" exactly when the queue
        // would report more than it returned.
        var (count, hasMore) = await _repository.CountOpenForReviewerAsync(_worldId, _gmId, WorldRole.GM, 3);

        var queue = await _repository.ListReviewQueueAsync(
            _worldId, [_gmSourceId, _playerSourceId], null, 3);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(hasMore, Is.True);
            Assert.That(queue.HasMore, Is.True, "the queue's has-more probe must agree");
        });
    }

    [Test]
    public async Task ProposalsFromAnotherWorld_AreNeverCounted()
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
        await Context.SaveChangesAsync();

        var foreignSourceId = Guid.NewGuid();
        Context.Sources.Add(new Source
        {
            Id = foreignSourceId,
            WorldId = otherWorldId,
            Type = SourceType.SessionNote,
            Title = "Another world's note",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _playerId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await Context.SaveChangesAsync();
        SeedBatchWithProposals(foreignSourceId, open: 4, decided: 0);
        await Context.SaveChangesAsync();

        var (gmCount, _) = await _repository.CountOpenForReviewerAsync(_worldId, _gmId, WorldRole.GM, 200);
        var (playerCount, _) = await _repository.CountOpenForReviewerAsync(_worldId, _playerId, WorldRole.Player, 200);

        Assert.Multiple(() =>
        {
            Assert.That(gmCount, Is.EqualTo(5));
            Assert.That(playerCount, Is.EqualTo(2), "the player authored the other world's source too");
        });
    }
}
