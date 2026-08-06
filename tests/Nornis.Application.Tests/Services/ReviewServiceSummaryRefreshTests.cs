using Nornis.Application.Application;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// What an accept asks the summary-refresh queue for. Real validator and real applicator:
/// the candidates come from the apply arms, so faking them would fake the subject —
/// including the rule these tests exist to pin, that an explicitly accepted summary
/// anywhere in the accept cancels the refresh for that artifact.
/// </summary>
[TestFixture]
public class ReviewServiceSummaryRefreshTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private FakeArtifactSummaryRefreshQueue _refreshQueue = null!;
    private ReviewService _service = null!;

    private Guid _worldId;
    private Guid _gmUserId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;
    private Artifact _artifact = null!;

    [SetUp]
    public void SetUp()
    {
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRepo = new InMemorySourceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();
        var sourceRefRepo = new InMemorySourceReferenceRepository();
        _refreshQueue = new FakeArtifactSummaryRefreshQueue();

        _service = new ReviewService(
            _proposalRepo,
            _batchRepo,
            _sourceRepo,
            _artifactRepo,
            _factRepo,
            relationshipRepo,
            sourceRefRepo,
            new FakeUnitOfWork(),
            new ProposalValidator(),
            new ProposalApplicator(
                _artifactRepo,
                _factRepo,
                relationshipRepo,
                sourceRefRepo,
                new InMemorySourceAttachmentRepository(),
                new InMemoryMapPlacemarkRepository(),
                new InMemoryWorldMemberRepository()),
            replayAdvancer: NoOpExtractionReplayAdvancer.Instance,
            summaryRefreshQueue: _refreshQueue);

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1: Black Harbor",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();

        _artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = ArtifactType.Character,
            Name = "Captain Voss",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _artifactRepo.Seed(_artifact);
    }

    private ReviewProposal SeedProposal(ReviewChangeType changeType, ReviewTargetType targetType, Guid? targetId, string json)
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = changeType,
            TargetType = targetType,
            TargetId = targetId,
            ProposedValueJson = json,
            Rationale = "Seen in the session.",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        _proposalRepo.CreateAsync(proposal).GetAwaiter().GetResult();
        return proposal;
    }

    private Task<Nornis.Application.Errors.AppResult<AcceptProposalResult>> AcceptAsync(ReviewProposal proposal) =>
        _service.AcceptProposalAsync(
            new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM), CancellationToken.None);

    private Task BatchAcceptAsync(params ReviewProposal[] proposals) =>
        _service.BatchAcceptAsync(
            new BatchAcceptCommand(proposals.Select(p => p.Id).ToList(), _worldId, _gmUserId, WorldRole.GM),
            CancellationToken.None);

    [Test]
    public async Task AcceptingAFact_RequestsARefreshForItsArtifact()
    {
        var proposal = SeedProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact, _artifact.Id,
            """{"predicate":"occupation","value":"smuggler","visibility":"PartyVisible"}""");

        await AcceptAsync(proposal);

        var request = _refreshQueue.Requests.Single();
        Assert.That(request.WorldId, Is.EqualTo(_worldId));
        Assert.That(request.ArtifactIds, Is.EquivalentTo([_artifact.Id]));
    }

    [Test]
    public async Task BatchAccept_CoalescesToOneRequestPerArtifact()
    {
        var first = SeedProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact, _artifact.Id,
            """{"predicate":"occupation","value":"smuggler","visibility":"PartyVisible"}""");
        var second = SeedProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact, _artifact.Id,
            """{"predicate":"location","value":"Black Harbor","visibility":"PartyVisible"}""");

        await BatchAcceptAsync(first, second);

        var request = _refreshQueue.Requests.Single();
        Assert.That(request.ArtifactIds, Is.EquivalentTo([_artifact.Id]),
            "two proposals on one artifact are one refresh, not two");
    }

    [Test]
    public async Task AnExplicitSummaryInTheSameAccept_CancelsTheRefreshTheFactsAskedFor()
    {
        var fact = SeedProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact, _artifact.Id,
            """{"predicate":"occupation","value":"smuggler","visibility":"PartyVisible"}""");
        var explicitSummary = SeedProposal(ReviewChangeType.UpdateArtifact, ReviewTargetType.Artifact, _artifact.Id,
            """{"summary":"The reviewer's own words about Voss."}""");

        await BatchAcceptAsync(fact, explicitSummary);

        Assert.That(_refreshQueue.Requests, Is.Empty,
            "the reviewer chose that text; regenerating right after would stomp it");
    }

    [Test]
    public async Task AVisibilityOnlyChange_RequestsNothing()
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = _artifact.Id,
            Predicate = "occupation",
            Value = "smuggler",
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.GMOnly,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        _factRepo.Seed(fact);
        var proposal = SeedProposal(ReviewChangeType.UpdateFact, ReviewTargetType.ArtifactFact, fact.Id,
            """{"visibility":"PartyVisible"}""");

        await AcceptAsync(proposal);

        Assert.That(_refreshQueue.Requests, Is.Empty,
            "who sees the record changed; what a summary would say did not");
    }

    [Test]
    public async Task AFailedAccept_RequestsNothing()
    {
        // Target artifact gone: the apply fails and rolls back, so no refresh may leak out.
        var proposal = SeedProposal(ReviewChangeType.AddFact, ReviewTargetType.ArtifactFact, Guid.NewGuid(),
            """{"predicate":"occupation","value":"smuggler","visibility":"PartyVisible"}""");

        var result = await AcceptAsync(proposal);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(_refreshQueue.Requests, Is.Empty);
    }
}
