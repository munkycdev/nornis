using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence.Repositories;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// Editing a proposal does not decide it — it still needs an accept or a reject, and it still
/// holds its batch open. The queue queries used to filter on Pending alone, so an edited
/// proposal vanished from the review screen while quietly blocking batch completion. These
/// run the real SQL predicates, which is where the bug lived.
/// </summary>
[TestFixture]
public class ReviewProposalQueueStatusTests : IntegrationTestBase
{
    private Guid _worldId;
    private Guid _sourceId;
    private Guid _batchId;
    private Guid _gmUserId;
    private ReviewProposalRepository _repository = null!;

    [SetUp]
    public async Task SetUp()
    {
        _worldId = Guid.NewGuid();
        _sourceId = Guid.NewGuid();
        _batchId = Guid.NewGuid();

        Context.ReviewProposals.RemoveRange(Context.ReviewProposals);
        Context.ReviewBatches.RemoveRange(Context.ReviewBatches);
        Context.Sources.RemoveRange(Context.Sources);
        Context.Worlds.RemoveRange(Context.Worlds);
        Context.Users.RemoveRange(Context.Users);
        await Context.SaveChangesAsync();

        var gmId = Guid.NewGuid();
        _gmUserId = gmId;
        Context.Users.Add(new User
        {
            Id = gmId,
            Auth0SubjectId = $"auth0|{gmId:N}",
            Username = "kelda",
            Email = "kelda@example.test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        });
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

        Context.Sources.Add(new Source
        {
            Id = _sourceId,
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 12",
            Body = "We questioned Captain Voss.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = gmId,
            CreatedAt = DateTimeOffset.UtcNow
        });
        Context.ReviewBatches.Add(new ReviewBatch
        {
            Id = _batchId,
            WorldId = _worldId,
            SourceId = _sourceId,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Context.SaveChangesAsync();

        _repository = new ReviewProposalRepository(Context);
    }

    [Test]
    public async Task ReviewQueue_IncludesEditedAlongsidePending()
    {
        var pending = await SeedProposalAsync(ReviewProposalStatus.Pending);
        var edited = await SeedProposalAsync(ReviewProposalStatus.Edited);
        var accepted = await SeedProposalAsync(ReviewProposalStatus.Accepted);
        var rejected = await SeedProposalAsync(ReviewProposalStatus.Rejected);

        var (proposals, hasMore) = await _repository.ListReviewQueueAsync(
            _worldId, [_sourceId], filterByBatchId: null, limit: 50);

        Assert.That(proposals.Select(p => p.Id), Is.EquivalentTo([pending, edited]));
        Assert.That(proposals.Select(p => p.Id), Does.Not.Contain(accepted));
        Assert.That(proposals.Select(p => p.Id), Does.Not.Contain(rejected));
        Assert.That(hasMore, Is.False);
    }

    [Test]
    public async Task ListPendingByWorld_IncludesEditedAlongsidePending()
    {
        var pending = await SeedProposalAsync(ReviewProposalStatus.Pending);
        var edited = await SeedProposalAsync(ReviewProposalStatus.Edited);
        await SeedProposalAsync(ReviewProposalStatus.Accepted);

        var open = await _repository.ListPendingByWorldAsync(_worldId);

        Assert.That(open.Select(p => p.Id), Is.EquivalentTo([pending, edited]));
    }

    [Test]
    public async Task ReviewQueue_BatchFilter_StillApplies()
    {
        var edited = await SeedProposalAsync(ReviewProposalStatus.Edited);

        var (matching, _) = await _repository.ListReviewQueueAsync(
            _worldId, [_sourceId], filterByBatchId: _batchId, limit: 50);
        var (other, _) = await _repository.ListReviewQueueAsync(
            _worldId, [_sourceId], filterByBatchId: Guid.NewGuid(), limit: 50);

        Assert.That(matching.Select(p => p.Id), Is.EquivalentTo([edited]));
        Assert.That(other, Is.Empty);
    }

    [Test]
    public async Task NewProposals_DefaultToNullAppliedToExistingArtifact()
    {
        var id = await SeedProposalAsync(ReviewProposalStatus.Pending);

        var stored = await _repository.GetByIdAsync(id);

        Assert.That(stored!.AppliedToExistingArtifact, Is.Null,
            "the column is additive: rows that predate apply-time dedup read as null");
    }

    [Test]
    public async Task CountOpenBySources_CountsPendingAndEditedOnly()
    {
        await SeedProposalAsync(ReviewProposalStatus.Pending);
        await SeedProposalAsync(ReviewProposalStatus.Edited);
        await SeedProposalAsync(ReviewProposalStatus.Accepted);
        await SeedProposalAsync(ReviewProposalStatus.Rejected);

        var counts = await _repository.CountOpenBySourcesAsync([_sourceId, Guid.NewGuid()]);

        Assert.Multiple(() =>
        {
            Assert.That(counts[_sourceId], Is.EqualTo(2));
            // A source with nothing open is simply absent — callers read it as zero.
            Assert.That(counts, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task CountOpenBySources_NoSources_IsEmpty()
    {
        await SeedProposalAsync(ReviewProposalStatus.Pending);

        var counts = await _repository.CountOpenBySourcesAsync([]);

        Assert.That(counts, Is.Empty);
    }

    [Test]
    public async Task CreatingAProposalAndThenUpdatingIt_IsOneSelfContainedRequest()
    {
        // Create left the instance tracked while every other method here reads AsNoTracking
        // and writes through Update(entity), so the update attached a second instance of the
        // same key and threw — deterministically, for anything that writes a proposal and
        // then decides it in one request, which is what a recovery accept does.
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batchId,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Ghost","type":"Concept"}""",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };

        await _repository.CreateAsync(proposal);

        var reloaded = (await _repository.GetByIdAsync(proposal.Id))!;
        reloaded.Accept(_gmUserId, DateTimeOffset.UtcNow);

        Assert.DoesNotThrowAsync(async () => await _repository.UpdateAsync(reloaded));
        Assert.That((await _repository.GetByIdAsync(proposal.Id))!.Status,
            Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    private async Task<Guid> SeedProposalAsync(ReviewProposalStatus status)
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batchId,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Captain Voss","type":"Character"}""",
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            RowVersion = []
        };

        Context.ReviewProposals.Add(proposal);
        await Context.SaveChangesAsync();
        return proposal.Id;
    }
}
