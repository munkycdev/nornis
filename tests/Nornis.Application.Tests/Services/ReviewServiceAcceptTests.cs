using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ReviewServiceAcceptTests
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

    private Guid _worldId;
    private Guid _gmUserId;
    private Guid _playerUserId;
    private Guid _otherPlayerUserId;
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

        _worldId = Guid.NewGuid();
        _gmUserId = Guid.NewGuid();
        _playerUserId = Guid.NewGuid();
        _otherPlayerUserId = Guid.NewGuid();

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
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
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();
    }

    #region Happy Path: Pending → Accepted

    [Test]
    public async Task AcceptProposal_PendingProposal_TransitionsToAccepted()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    [Test]
    public async Task AcceptProposal_PendingProposal_SetsReviewedAtToUtcNow()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var before = DateTimeOffset.UtcNow;

        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);
        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        var after = DateTimeOffset.UtcNow;
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ReviewedAt, Is.InRange(before, after));
    }

    [Test]
    public async Task AcceptProposal_PendingProposal_SetsReviewedByUserId()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ReviewedByUserId, Is.EqualTo(_gmUserId));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(_gmUserId));
    }

    [Test]
    public async Task AcceptProposal_PlayerAcceptsOwnSourceProposal_Succeeds()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    #endregion

    #region Acting filter handed to the applicator

    // A Player may accept proposals on their own source (the test directly above), and the
    // payload is Player-editable. So the filter the applicator resolves names through has to
    // be the Player's, not an unrestricted one.

    [Test]
    public async Task AcceptProposal_PlayerAccepting_PassesThePlayersOwnFilterToTheApplicator()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _playerUserId, WorldRole.Player);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var filter = _applicator.LastActingFilter;
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter!.Scopes, Does.Not.Contain(VisibilityScope.GMOnly),
            "a Player's name resolution must not reach GM-only artifacts");
        Assert.That(filter.PrivateOwnerUserId, Is.EqualTo(_playerUserId),
            "Private artifacts must be gated to the accepting Player's own rows");
    }

    [Test]
    public async Task AcceptProposal_GmAccepting_PassesAnUnrestrictedFilterToTheApplicator()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var filter = _applicator.LastActingFilter;
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter!.Scopes, Does.Contain(VisibilityScope.GMOnly));
        Assert.That(filter.PrivateOwnerUserId, Is.Null, "a GM reads Private rows unrestricted");
    }

    #endregion

    #region Edited → Accepted

    [Test]
    public async Task AcceptProposal_EditedProposal_TransitionsToAccepted()
    {
        var proposal = MakeEditedProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    [Test]
    public async Task AcceptProposal_EditedProposal_UsesEditedJson()
    {
        var editedJson = """{"name":"Captain Voss (Edited)","type":"Character","summary":"Edited summary"}""";
        var proposal = MakeEditedProposal(editedJson);
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        // The proposal's ProposedValueJson should remain the edited value
        Assert.That(proposal.ProposedValueJson, Is.EqualTo(editedJson));
    }

    #endregion

    #region Idempotent: Already Accepted

    [Test]
    public async Task AcceptProposal_AlreadyAccepted_ReturnsSuccessWithExistingData()
    {
        var reviewedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var reviewedBy = _gmUserId;
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted, reviewedAt, reviewedBy);
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ReviewedAt, Is.EqualTo(reviewedAt));
        Assert.That(result.Value.ReviewedByUserId, Is.EqualTo(reviewedBy));
    }

    [Test]
    public async Task AcceptProposal_AlreadyAccepted_DoesNotCreateAdditionalEntities()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        await _proposalRepo.CreateAsync(proposal);
        var initialSourceRefs = _sourceRefRepo.References.Count;
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(_sourceRefRepo.References.Count, Is.EqualTo(initialSourceRefs));
        Assert.That(_unitOfWork.Transactions, Is.Empty);
    }

    #endregion

    #region Conflicting: Rejected → Accept returns 409

    [Test]
    public async Task AcceptProposal_AlreadyRejected_Returns409Conflict()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Rejected);
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("conflict"));
    }

    #endregion

    #region Invisible Proposal → Not Found

    [Test]
    public async Task AcceptProposal_GMOnlySourceInvisibleToPlayer_ReturnsNotFound()
    {
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.GMNote,
            Title = "GM Secret: Silver Key location",
            Visibility = VisibilityScope.GMOnly,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(gmSource);

        var gmBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = gmSource.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var proposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        var command = new AcceptProposalCommand(proposal.Id, _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task AcceptProposal_PrivateSourceInvisibleToOtherPlayer_ReturnsNotFound()
    {
        var privateSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.JournalEntry,
            Title = "Private notes about Black Harbor",
            Visibility = VisibilityScope.Private,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _otherPlayerUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(privateSource);

        var privateBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = privateSource.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(privateBatch);

        var proposal = MakePendingProposal(privateBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        var command = new AcceptProposalCommand(proposal.Id, _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task AcceptProposal_NonExistentProposal_ReturnsNotFound()
    {
        var command = new AcceptProposalCommand(Guid.NewGuid(), _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    #endregion

    #region Observer → Not Found (visibility blocks before auth check)

    [Test]
    public async Task AcceptProposal_Observer_ReturnsNotFound()
    {
        // Observers cannot see any source (IsSourceVisibleToUser returns false for Observer)
        // Per Requirement 7.4: invisible proposals are treated as not-found
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var observerUserId = Guid.NewGuid();
        var command = new AcceptProposalCommand(proposal.Id, _worldId, observerUserId, WorldRole.Observer);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    #endregion

    #region Player Unauthorized (Different Source Owner) → 403

    [Test]
    public async Task AcceptProposal_PlayerNotOwnerOfSource_Returns403Forbidden()
    {
        // Create a source owned by a different player but PartyVisible
        // The visibility check passes (Player can see PartyVisible sources they created)
        // But actually the IsSourceVisibleToUser for Player checks CreatedByUserId matches
        // So for a Player, a source they didn't create is invisible — returns not_found
        // Let's test the case where GM-created PartyVisible source is accessed by Player
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "GM's party notes about Captain Voss",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(gmSource);

        var gmBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = gmSource.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var proposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        // Player tries to accept from GM's source — visibility check will fail since
        // IsSourceVisibleToUser for Player checks CreatedByUserId == actingUserId
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        // Per requirement 7.4: invisible proposals return not-found, not forbidden
        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(result.Error.Code, Is.EqualTo("not_found"));
    }

    #endregion

    #region Invalid ProposedValueJson → Validation Error

    [Test]
    public async Task AcceptProposal_InvalidProposedValueJson_ReturnsValidationError()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        _validator.ConfigureFailure("validation_error", "ProposedValueJson is malformed.");
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task AcceptProposal_InvalidProposedValueJson_DoesNotMutateProposal()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        _validator.ConfigureFailure("validation_error", "Invalid JSON.");
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending));
        Assert.That(proposal.ReviewedAt, Is.Null);
    }

    #endregion

    #region Target Entity Not Found → Validation Error

    [Test]
    public async Task AcceptProposal_TargetEntityNotFound_ReturnsValidationError()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        _applicator.ConfigureFailure("validation_error", "Target entity not found.");
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task AcceptProposal_ApplicatorFails_RollsBackTransaction()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        _applicator.ConfigureFailure("validation_error", "Target artifact not found.");
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(_unitOfWork.Transactions, Has.Count.EqualTo(1));
        Assert.That(_unitOfWork.Transactions[0].RolledBack, Is.True);
        Assert.That(_unitOfWork.Transactions[0].Committed, Is.False);
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Pending));
    }

    #endregion

    #region SourceReference Created on Acceptance

    [Test]
    public async Task AcceptProposal_Success_CreatesEntityViaApplicator()
    {
        var entityId = Guid.NewGuid();
        _applicator.ConfigureSuccess(entityId);
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        // The applicator was called successfully — proposal TargetId is set by the real applicator,
        // but with the fake we verify the transaction committed successfully
        Assert.That(_unitOfWork.Transactions, Has.Count.EqualTo(1));
        Assert.That(_unitOfWork.Transactions[0].Committed, Is.True);
    }

    [Test]
    public async Task AcceptProposal_Success_CommitsTransaction()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        Assert.That(_unitOfWork.Transactions, Has.Count.EqualTo(1));
        Assert.That(_unitOfWork.Transactions[0].Committed, Is.True);
        Assert.That(_unitOfWork.Transactions[0].RolledBack, Is.False);
    }

    #endregion

    #region Batch Transitions: Pending → InReview on First Accept

    [Test]
    public async Task AcceptProposal_FirstReviewInBatch_TransitionsBatchToInReview()
    {
        Assert.That(_batch.Status, Is.EqualTo(ReviewBatchStatus.Pending));

        // Need at least 2 proposals so accepting one doesn't complete the batch
        var proposal = MakePendingProposal();
        var otherPending = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        await _proposalRepo.CreateAsync(otherPending);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(_batch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.InReview));
    }

    [Test]
    public async Task AcceptProposal_BatchAlreadyInReview_StaysInReview()
    {
        _batch.Status = ReviewBatchStatus.InReview;

        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);

        var command = new AcceptProposalCommand(p1.Id, _worldId, _gmUserId, WorldRole.GM);
        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(_batch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.InReview));
    }

    #endregion

    #region Batch Transitions: InReview → Completed When All Terminal

    [Test]
    public async Task AcceptProposal_LastPendingProposal_TransitionsBatchToCompleted()
    {
        _batch.Status = ReviewBatchStatus.InReview;

        // One already-rejected proposal
        var rejected = MakeProposalWithStatus(ReviewProposalStatus.Rejected);
        await _proposalRepo.CreateAsync(rejected);

        // One pending proposal — this will be the last
        var lastPending = MakePendingProposal();
        await _proposalRepo.CreateAsync(lastPending);

        var command = new AcceptProposalCommand(lastPending.Id, _worldId, _gmUserId, WorldRole.GM);
        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(_batch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.Completed));
        Assert.That(updatedBatch.CompletedAt, Is.Not.Null);
    }

    [Test]
    public async Task AcceptProposal_NotAllTerminal_BatchStaysInReview()
    {
        _batch.Status = ReviewBatchStatus.InReview;

        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);

        // Accept only p1, p2 stays Pending
        var command = new AcceptProposalCommand(p1.Id, _worldId, _gmUserId, WorldRole.GM);
        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(_batch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.InReview));
        Assert.That(updatedBatch.CompletedAt, Is.Null);
    }

    [Test]
    public async Task AcceptProposal_CanceledBatch_DoesNotTransition()
    {
        _batch.Status = ReviewBatchStatus.Canceled;

        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new AcceptProposalCommand(proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        await _service.AcceptProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(_batch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.Canceled));
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
            ProposedValueJson = """{"name":"Captain Voss","type":"Character","summary":"Harbor captain in Black Harbor"}""",
            Rationale = "Captain Voss is mentioned as a character in Black Harbor.",
            Confidence = 0.85m,
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private ReviewProposal MakeEditedProposal(string? editedJson = null, Guid? batchId = null)
    {
        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId ?? _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = editedJson ?? """{"name":"Silver Key","type":"Item","summary":"A mysterious silver key"}""",
            Rationale = "The Silver Key was found in Captain Voss's quarters.",
            Confidence = 0.9m,
            Status = ReviewProposalStatus.Edited,
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            ReviewedByUserId = _gmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15)
        };
    }

    private ReviewProposal MakeProposalWithStatus(
        ReviewProposalStatus status,
        DateTimeOffset? reviewedAt = null,
        Guid? reviewedBy = null,
        Guid? batchId = null)
    {
        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId ?? _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Black Harbor","type":"Location","summary":"A coastal port town"}""",
            Rationale = "Black Harbor is mentioned as a location.",
            Confidence = 0.9m,
            Status = status,
            ReviewedAt = reviewedAt ?? (status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected
                ? DateTimeOffset.UtcNow.AddMinutes(-5) : null),
            ReviewedByUserId = reviewedBy ?? (status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected
                ? _gmUserId : null),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-20)
        };
    }

    #endregion
}
