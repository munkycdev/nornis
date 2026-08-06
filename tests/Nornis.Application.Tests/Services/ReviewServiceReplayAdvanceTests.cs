using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The review pipeline is one of the two replay-advance triggers: when a batch's LAST
/// proposal reaches a terminal state, the advancer is nudged with the batch's source.
/// Partially reviewed batches and named sweep batches (Kind != null) never advance.
/// </summary>
[TestFixture]
public class ReviewServiceReplayAdvanceTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private FakeExtractionReplayAdvancer _advancer = null!;
    private ReviewService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;
    private Source _source = null!;

    [SetUp]
    public void SetUp()
    {
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRepo = new InMemorySourceRepository();
        _advancer = new FakeExtractionReplayAdvancer();

        _service = new ReviewService(
            _proposalRepo,
            _batchRepo,
            _sourceRepo,
            new InMemoryArtifactRepository(),
            new InMemoryArtifactFactRepository(),
            new InMemoryArtifactRelationshipRepository(),
            new InMemorySourceReferenceRepository(),
            new FakeUnitOfWork(),
            new FakeProposalValidator(),
            new FakeProposalApplicator(),
            _advancer,
            NoOpArtifactSummaryRefreshQueue.Instance);

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1",
            Body = "We questioned Captain Voss.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);
    }

    private ReviewBatch SeedBatch(string? kind = null)
    {
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.Pending,
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(batch).GetAwaiter().GetResult();
        return batch;
    }

    private ReviewProposal SeedProposal(Guid batchId)
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Captain Voss","type":"Character"}""",
            Rationale = "Mentioned in the session.",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _proposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        return proposal;
    }

    [Test]
    public async Task AcceptingTheLastProposal_NudgesTheAdvancerWithTheBatchSource()
    {
        var batch = SeedBatch();
        var proposal = SeedProposal(batch.Id);

        await _service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(_advancer.Calls, Is.EqualTo([(_worldId, _source.Id)]));
    }

    [Test]
    public async Task RejectingTheLastProposal_AlsoNudgesTheAdvancer()
    {
        var batch = SeedBatch();
        var proposal = SeedProposal(batch.Id);

        await _service.RejectProposalAsync(
            new RejectProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(_advancer.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ResolvingOnlySomeProposals_DoesNotAdvance()
    {
        var batch = SeedBatch();
        var first = SeedProposal(batch.Id);
        SeedProposal(batch.Id);

        await _service.AcceptProposalAsync(
            new AcceptProposalCommand(first.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(_advancer.Calls, Is.Empty);
    }

    [Test]
    public async Task ResolvingTheRestOfTheBatch_AdvancesExactlyOnce()
    {
        var batch = SeedBatch();
        var first = SeedProposal(batch.Id);
        var second = SeedProposal(batch.Id);

        await _service.AcceptProposalAsync(
            new AcceptProposalCommand(first.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);
        await _service.AcceptProposalAsync(
            new AcceptProposalCommand(second.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(_advancer.Calls, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task NamedSweepBatch_NeverAdvancesTheReplay()
    {
        var batch = SeedBatch(kind: "RelationshipBackfill");
        var proposal = SeedProposal(batch.Id);

        await _service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);

        Assert.That(_advancer.Calls, Is.Empty);
    }
}
