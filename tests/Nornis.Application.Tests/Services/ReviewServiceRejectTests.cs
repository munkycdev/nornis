using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ReviewServiceRejectTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private FakeProposalValidator _validator = null!;
    private FakeProposalApplicator _applicator = null!;
    private ReviewService _service = null!;

    private Guid _campaignId;
    private Guid _gmUserId;
    private Guid _playerUserId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;

    [SetUp]
    public void SetUp()
    {
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _sourceRepo = new InMemorySourceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _unitOfWork = new FakeUnitOfWork();
        _validator = new FakeProposalValidator();
        _applicator = new FakeProposalApplicator();

        _service = new ReviewService(
            _proposalRepo,
            _batchRepo,
            _sourceRepo,
            _artifactRepo,
            _factRepo,
            _relationshipRepo,
            _sourceRefRepo,
            _unitOfWork,
            _validator,
            _applicator);

        _campaignId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
        _playerUserId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            Type = SourceType.SessionNote,
            Title = "Session 1: Black Harbor",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _playerUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();
    }

    #region Happy Path: Pending → Rejected with correct metadata

    [Test]
    public async Task RejectProposal_Pending_TransitionsToRejectedWithCorrectMetadata()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var beforeReject = DateTimeOffset.UtcNow;

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProposalId, Is.EqualTo(proposal.Id));
        Assert.That(result.Value.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(result.Value.ReviewedByUserId, Is.EqualTo(_gmUserId));
        Assert.That(result.Value.ReviewedAt, Is.GreaterThanOrEqualTo(beforeReject));
        Assert.That(result.Value.ReviewedAt, Is.LessThanOrEqualTo(DateTimeOffset.UtcNow));

        // Verify proposal entity updated
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(_gmUserId));
        Assert.That(proposal.ReviewedAt, Is.Not.Null);
    }

    [Test]
    public async Task RejectProposal_PlayerRejectsOwnSourceProposal_Succeeds()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _playerUserId, CampaignRole.Player);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(result.Value.ReviewedByUserId, Is.EqualTo(_playerUserId));
    }

    #endregion

    #region Edited → Rejected succeeds

    [Test]
    public async Task RejectProposal_Edited_TransitionsToRejected()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Edited);
        await _proposalRepo.CreateAsync(proposal);

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
    }

    #endregion

    #region No knowledge graph changes on rejection

    [Test]
    public async Task RejectProposal_DoesNotCreateArtifacts()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var artifactCountBefore = _artifactRepo.Artifacts.Count;

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(_artifactRepo.Artifacts.Count, Is.EqualTo(artifactCountBefore));
    }

    [Test]
    public async Task RejectProposal_DoesNotCreateArtifactFacts()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var factCountBefore = _factRepo.Facts.Count;

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(_factRepo.Facts.Count, Is.EqualTo(factCountBefore));
    }

    [Test]
    public async Task RejectProposal_DoesNotCreateArtifactRelationships()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var relationshipCountBefore = _relationshipRepo.Relationships.Count;

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(_relationshipRepo.Relationships.Count, Is.EqualTo(relationshipCountBefore));
    }

    [Test]
    public async Task RejectProposal_DoesNotCreateSourceReferences()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var refCountBefore = _sourceRefRepo.References.Count;

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(_sourceRefRepo.References.Count, Is.EqualTo(refCountBefore));
    }

    #endregion

    #region Idempotent: already Rejected returns success

    [Test]
    public async Task RejectProposal_AlreadyRejected_ReturnsSuccessWithExistingMetadata()
    {
        var existingReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var existingReviewerId = _gmUserId;

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Captain Voss","type":"Character"}""",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Rejected,
            ReviewedAt = existingReviewedAt,
            ReviewedByUserId = existingReviewerId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        await _proposalRepo.CreateAsync(proposal);

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProposalId, Is.EqualTo(proposal.Id));
        Assert.That(result.Value.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(result.Value.ReviewedAt, Is.EqualTo(existingReviewedAt));
        Assert.That(result.Value.ReviewedByUserId, Is.EqualTo(existingReviewerId));
    }

    [Test]
    public async Task RejectProposal_AlreadyRejected_DoesNotModifyProposal()
    {
        var existingReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-10);

        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Silver Key","type":"Item"}""",
            Confidence = 0.9m,
            Status = ReviewProposalStatus.Rejected,
            ReviewedAt = existingReviewedAt,
            ReviewedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        await _proposalRepo.CreateAsync(proposal);

        var anotherUserId = Guid.NewGuid();
        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        // ReviewedAt and ReviewedByUserId should remain unchanged
        Assert.That(proposal.ReviewedAt, Is.EqualTo(existingReviewedAt));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(_gmUserId));
    }

    #endregion

    #region Conflicting: Accepted → reject returns 409 error

    [Test]
    public async Task RejectProposal_AlreadyAccepted_Returns409Conflict()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        await _proposalRepo.CreateAsync(proposal);

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("conflict"));
    }

    [Test]
    public async Task RejectProposal_AlreadyAccepted_DoesNotChangeStatus()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        await _proposalRepo.CreateAsync(proposal);

        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    #endregion

    #region Invisible proposal returns not-found

    [Test]
    public async Task RejectProposal_GMOnlySourceInvisibleToPlayer_ReturnsNotFound()
    {
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            Type = SourceType.GMNote,
            Title = "GM secrets about Black Harbor",
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(gmSource);

        var gmBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            SourceId = gmSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var proposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        // Player cannot see GMOnly source
        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _playerUserId, CampaignRole.Player);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task RejectProposal_PrivateSourceInvisibleToOtherPlayer_ReturnsNotFound()
    {
        var otherPlayerId = Guid.NewGuid();
        var privateSource = new Source
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            Type = SourceType.JournalEntry,
            Title = "Captain Voss private notes",
            Visibility = VisibilityScope.Private,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = otherPlayerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(privateSource);

        var privateBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            SourceId = privateSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(privateBatch);

        var proposal = MakePendingProposal(privateBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        // Player cannot see another player's private source
        var command = new RejectProposalCommand(
            proposal.Id, _campaignId, _playerUserId, CampaignRole.Player);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task RejectProposal_NonExistentProposal_ReturnsNotFound()
    {
        var nonExistentId = Guid.NewGuid();
        var command = new RejectProposalCommand(
            nonExistentId, _campaignId, _gmUserId, CampaignRole.GM);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task RejectProposal_ProposalInDifferentCampaign_ReturnsNotFound()
    {
        var otherCampaignId = Guid.NewGuid();
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        // Use a different campaignId than the batch's campaign
        var command = new RejectProposalCommand(
            proposal.Id, otherCampaignId, _gmUserId, CampaignRole.GM);

        var result = await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    #endregion

    #region Batch transitions on first rejection

    [Test]
    public async Task RejectProposal_FirstRejectionInPendingBatch_TransitionsBatchToInReview()
    {
        var pendingBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15)
        };
        await _batchRepo.CreateAsync(pendingBatch);

        var proposalToReject = MakePendingProposal(pendingBatch.Id);
        var otherPendingProposal = MakePendingProposal(pendingBatch.Id);
        await _proposalRepo.CreateAsync(proposalToReject);
        await _proposalRepo.CreateAsync(otherPendingProposal);

        var command = new RejectProposalCommand(
            proposalToReject.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(pendingBatch.Status, Is.EqualTo(ReviewBatchStatus.InReview));
    }

    [Test]
    public async Task RejectProposal_LastProposalRejected_TransitionsBatchToCompleted()
    {
        var pendingBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            CampaignId = _campaignId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15)
        };
        await _batchRepo.CreateAsync(pendingBatch);

        // One already-accepted proposal
        var acceptedProposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = pendingBatch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Black Harbor","type":"Location"}""",
            Confidence = 0.9m,
            Status = ReviewProposalStatus.Accepted,
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReviewedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
        await _proposalRepo.CreateAsync(acceptedProposal);

        // One pending proposal — this is the last one to reach terminal
        var lastProposal = MakePendingProposal(pendingBatch.Id);
        await _proposalRepo.CreateAsync(lastProposal);

        var command = new RejectProposalCommand(
            lastProposal.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(pendingBatch.Status, Is.EqualTo(ReviewBatchStatus.Completed));
        Assert.That(pendingBatch.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task RejectProposal_BatchAlreadyInReview_StaysInReviewWhenOthersRemain()
    {
        // Batch already InReview, another proposal still pending
        var anotherPending = MakePendingProposal(_batch.Id);
        var proposalToReject = MakePendingProposal(_batch.Id);
        await _proposalRepo.CreateAsync(anotherPending);
        await _proposalRepo.CreateAsync(proposalToReject);

        var command = new RejectProposalCommand(
            proposalToReject.Id, _campaignId, _gmUserId, CampaignRole.GM);

        await _service.RejectProposalAsync(command, CancellationToken.None);

        Assert.That(_batch.Status, Is.EqualTo(ReviewBatchStatus.InReview));
    }

    #endregion

    #region Helpers

    private ReviewProposal MakePendingProposal(Guid? batchId = null)
    {
        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId ?? _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Captain Voss","type":"Character"}""",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private ReviewProposal MakeProposalWithStatus(ReviewProposalStatus status, Guid? batchId = null)
    {
        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId ?? _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Silver Key","type":"Item"}""",
            Confidence = 0.9m,
            Status = status,
            ReviewedAt = status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected
                ? DateTimeOffset.UtcNow.AddMinutes(-5) : null,
            ReviewedByUserId = status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected
                ? _gmUserId : null,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    #endregion
}
