using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ReviewServiceBatchTests
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

        _source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Session 1: Black Harbor",
            Body = "We questioned Captain Voss.",
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
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();
    }

    #region BatchAcceptAsync — Validation

    [Test]
    public async Task BatchAccept_EmptyList_ReturnsValidationError()
    {
        var command = new BatchAcceptCommand([], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task BatchAccept_MoreThan50_ReturnsValidationError()
    {
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        var command = new BatchAcceptCommand(ids, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task BatchAccept_DuplicateIds_ProcessesEachOnce()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var ids = new List<Guid> { proposal.Id, proposal.Id, proposal.Id };
        var command = new BatchAcceptCommand(ids, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    #endregion

    #region BatchAcceptAsync — Accept Logic

    [Test]
    public async Task BatchAccept_PendingProposals_AcceptsAll()
    {
        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);
        var command = new BatchAcceptCommand(
            [p1.Id, p2.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.EquivalentTo(new[] { p1.Id, p2.Id }));
        Assert.That(result.Value.Failed, Is.Empty);
        Assert.That(p1.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(p2.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    [Test]
    public async Task BatchAccept_AlreadyAccepted_IdempotentSuccess()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        await _proposalRepo.CreateAsync(proposal);
        var command = new BatchAcceptCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(proposal.Id));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    [Test]
    public async Task BatchAccept_AlreadyRejected_ReportsConflict()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Rejected);
        await _proposalRepo.CreateAsync(proposal);
        var command = new BatchAcceptCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("conflict"));
    }

    #endregion

    #region BatchAcceptAsync — Visibility and Authorization

    [Test]
    public async Task BatchAccept_InvisibleProposal_ReportsNotFound()
    {
        // GMOnly source invisible to Player
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.GMNote,
            Title = "GM Secret",
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
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var proposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        // Player can't see GMOnly source
        var command = new BatchAcceptCommand(
            [proposal.Id], _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task BatchAccept_PlayerCannotSeeOtherPlayersSource_ReportsNotFound()
    {
        // Source owned by another player — Player visibility check fails first
        // (Players can only see sources they created), so this is reported as not_found
        // per Requirement 7.4: invisible proposals treated as not-found
        var otherPlayer = Guid.NewGuid();
        var otherSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Other's Notes",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = otherPlayer,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(otherSource);

        var otherBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = otherSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(otherBatch);

        var proposal = MakePendingProposal(otherBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        // Player trying to accept proposal from another player's source
        var command = new BatchAcceptCommand(
            [proposal.Id], _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("not_found"));
    }

    #endregion

    #region BatchAcceptAsync — Partial Failures

    [Test]
    public async Task BatchAccept_MixOfSuccessAndFailure_PartitionsCorrectly()
    {
        var goodProposal = MakePendingProposal();
        var rejectedProposal = MakeProposalWithStatus(ReviewProposalStatus.Rejected);
        var nonExistentId = Guid.NewGuid();

        await _proposalRepo.CreateAsync(goodProposal);
        await _proposalRepo.CreateAsync(rejectedProposal);

        var command = new BatchAcceptCommand(
            [goodProposal.Id, rejectedProposal.Id, nonExistentId],
            _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(goodProposal.Id));
        Assert.That(result.Value.Failed, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task BatchAccept_ProcessesInRequestOrder()
    {
        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        var p3 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);
        await _proposalRepo.CreateAsync(p3);

        var command = new BatchAcceptCommand(
            [p3.Id, p1.Id, p2.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        // All succeed in request order
        Assert.That(result.Value!.Succeeded, Is.EqualTo(new[] { p3.Id, p1.Id, p2.Id }));
    }

    #endregion

    #region BatchRejectAsync — Validation

    [Test]
    public async Task BatchReject_EmptyList_ReturnsValidationError()
    {
        var command = new BatchRejectCommand([], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task BatchReject_MoreThan50_ReturnsValidationError()
    {
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList();
        var command = new BatchRejectCommand(ids, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task BatchReject_DuplicateIds_ProcessesEachOnce()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var ids = new List<Guid> { proposal.Id, proposal.Id };
        var command = new BatchRejectCommand(ids, _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    #endregion

    #region BatchRejectAsync — Reject Logic

    [Test]
    public async Task BatchReject_PendingProposals_RejectsAll()
    {
        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);
        var command = new BatchRejectCommand(
            [p1.Id, p2.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.EquivalentTo(new[] { p1.Id, p2.Id }));
        Assert.That(result.Value.Failed, Is.Empty);
        Assert.That(p1.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(p2.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
    }

    [Test]
    public async Task BatchReject_AlreadyRejected_IdempotentSuccess()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Rejected);
        await _proposalRepo.CreateAsync(proposal);
        var command = new BatchRejectCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(proposal.Id));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    [Test]
    public async Task BatchReject_AlreadyAccepted_ReportsConflict()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        await _proposalRepo.CreateAsync(proposal);
        var command = new BatchRejectCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("conflict"));
    }

    #endregion

    #region BatchRejectAsync — Visibility

    [Test]
    public async Task BatchReject_InvisibleProposal_ReportsNotFound()
    {
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.GMNote,
            Title = "GM Secret",
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
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var proposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        var command = new BatchRejectCommand(
            [proposal.Id], _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("not_found"));
    }

    #endregion

    #region BatchAcceptAsync — Transaction Failure

    [Test]
    public async Task BatchAccept_TransactionFails_ReportsFailureAndContinues()
    {
        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);

        // First transaction will fail, second will succeed
        _unitOfWork.ConfigureCommitFailure(true);

        var command = new BatchAcceptCommand(
            [p1.Id, p2.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        // Both should fail due to commit failure
        Assert.That(result.Value!.Failed, Has.Count.EqualTo(2));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("transaction_failed"));
    }

    #endregion

    #region BatchAcceptAsync — Authorization Per Proposal

    [Test]
    public async Task BatchAccept_GmAuthorizedForAllSources_AcceptsProposalsFromDifferentOwners()
    {
        // Create a second source owned by a different user
        var otherPlayerId = Guid.NewGuid();
        var otherSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.SessionNote,
            Title = "Tavrin's Notes on Silver Key",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = otherPlayerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(otherSource);

        var otherBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = otherSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(otherBatch);

        var proposalFromPlayer = MakePendingProposal(_batch.Id);
        var proposalFromOther = MakePendingProposal(otherBatch.Id);
        await _proposalRepo.CreateAsync(proposalFromPlayer);
        await _proposalRepo.CreateAsync(proposalFromOther);

        // GM can accept proposals from both sources
        var command = new BatchAcceptCommand(
            [proposalFromPlayer.Id, proposalFromOther.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(2));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    [Test]
    public async Task BatchAccept_PlayerAuthorizedOnlyForOwnSource_PartialFailure()
    {
        // Player owns _source. Create another source owned by GM.
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.GMNote,
            Title = "GM Notes: Black Harbor Secrets",
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
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var ownProposal = MakePendingProposal(_batch.Id);
        var gmProposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(ownProposal);
        await _proposalRepo.CreateAsync(gmProposal);

        // Player can only accept from own source — gmProposal is not visible to player
        var command = new BatchAcceptCommand(
            [ownProposal.Id, gmProposal.Id], _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(ownProposal.Id));
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].ProposalId, Is.EqualTo(gmProposal.Id));
    }

    #endregion

    #region BatchRejectAsync — Partial Failures

    [Test]
    public async Task BatchReject_MixOfSuccessAndFailure_PartitionsCorrectly()
    {
        var goodProposal = MakePendingProposal();
        var acceptedProposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        var nonExistentId = Guid.NewGuid();

        await _proposalRepo.CreateAsync(goodProposal);
        await _proposalRepo.CreateAsync(acceptedProposal);

        var command = new BatchRejectCommand(
            [goodProposal.Id, acceptedProposal.Id, nonExistentId],
            _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(goodProposal.Id));
        Assert.That(result.Value.Failed, Has.Count.EqualTo(2));

        var acceptedFailure = result.Value.Failed.First(f => f.ProposalId == acceptedProposal.Id);
        Assert.That(acceptedFailure.Code, Is.EqualTo("conflict"));

        var notFoundFailure = result.Value.Failed.First(f => f.ProposalId == nonExistentId);
        Assert.That(notFoundFailure.Code, Is.EqualTo("not_found"));
    }

    [Test]
    public async Task BatchReject_ProcessesInRequestOrder()
    {
        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        var p3 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);
        await _proposalRepo.CreateAsync(p3);

        var command = new BatchRejectCommand(
            [p3.Id, p1.Id, p2.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.EqualTo(new[] { p3.Id, p1.Id, p2.Id }));
    }

    #endregion

    #region BatchRejectAsync — Authorization Per Proposal

    [Test]
    public async Task BatchReject_GmAuthorizedForAllSources_RejectsProposalsFromDifferentOwners()
    {
        var otherPlayerId = Guid.NewGuid();
        var otherSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.JournalEntry,
            Title = "Tavrin's Journal: Silver Key Discovery",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = otherPlayerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(otherSource);

        var otherBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = otherSource.Id,
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(otherBatch);

        var proposalFromPlayer = MakePendingProposal(_batch.Id);
        var proposalFromOther = MakePendingProposal(otherBatch.Id);
        await _proposalRepo.CreateAsync(proposalFromPlayer);
        await _proposalRepo.CreateAsync(proposalFromOther);

        var command = new BatchRejectCommand(
            [proposalFromPlayer.Id, proposalFromOther.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(2));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    [Test]
    public async Task BatchReject_PlayerAuthorizedOnlyForOwnSource_PartialFailure()
    {
        var gmSource = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            Type = SourceType.GMNote,
            Title = "GM Notes: Captain Voss Intel",
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
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await _batchRepo.CreateAsync(gmBatch);

        var ownProposal = MakePendingProposal(_batch.Id);
        var gmProposal = MakePendingProposal(gmBatch.Id);
        await _proposalRepo.CreateAsync(ownProposal);
        await _proposalRepo.CreateAsync(gmProposal);

        // Player can only reject proposals from own source
        var command = new BatchRejectCommand(
            [ownProposal.Id, gmProposal.Id], _worldId, _playerUserId, WorldRole.Player);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(ownProposal.Id));
        Assert.That(result.Value.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].ProposalId, Is.EqualTo(gmProposal.Id));
    }

    #endregion

    #region BatchAcceptAsync — Batch Size Edge Cases

    [Test]
    public async Task BatchAccept_ExactlyOneProposal_Succeeds()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new BatchAcceptCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BatchAccept_Exactly50Proposals_Succeeds()
    {
        var proposals = new List<ReviewProposal>();
        for (int i = 0; i < 50; i++)
        {
            var p = MakePendingProposal();
            await _proposalRepo.CreateAsync(p);
            proposals.Add(p);
        }
        var command = new BatchAcceptCommand(
            proposals.Select(p => p.Id).ToList(), _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(50));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    #endregion

    #region BatchRejectAsync — Batch Size Edge Cases

    [Test]
    public async Task BatchReject_ExactlyOneProposal_Succeeds()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);
        var command = new BatchRejectCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task BatchReject_Exactly50Proposals_Succeeds()
    {
        var proposals = new List<ReviewProposal>();
        for (int i = 0; i < 50; i++)
        {
            var p = MakePendingProposal();
            await _proposalRepo.CreateAsync(p);
            proposals.Add(p);
        }
        var command = new BatchRejectCommand(
            proposals.Select(p => p.Id).ToList(), _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Has.Count.EqualTo(50));
        Assert.That(result.Value.Failed, Is.Empty);
    }

    #endregion

    #region BatchAcceptAsync — Applies Single-Proposal Logic Per Item

    [Test]
    public async Task BatchAccept_ValidatesEachProposalIndividually()
    {
        // Configure validator to fail — each proposal should be validated
        _validator.ConfigureFailure("validation_error", "Malformed payload");

        var p1 = MakePendingProposal();
        var p2 = MakePendingProposal();
        await _proposalRepo.CreateAsync(p1);
        await _proposalRepo.CreateAsync(p2);

        var command = new BatchAcceptCommand(
            [p1.Id, p2.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Is.Empty);
        Assert.That(result.Value.Failed, Has.Count.EqualTo(2));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("validation_error"));
        Assert.That(result.Value.Failed[1].Code, Is.EqualTo("validation_error"));
    }

    [Test]
    public async Task BatchAccept_EditedProposalProcessedLikeSingleAccept()
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact,
            ProposedValueJson = """{"name":"Black Harbor","type":"Location"}""",
            Confidence = 0.9m,
            Status = ReviewProposalStatus.Edited,
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ReviewedByUserId = _playerUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        await _proposalRepo.CreateAsync(proposal);

        var command = new BatchAcceptCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchAcceptAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(proposal.Id));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(_gmUserId));
    }

    #endregion

    #region BatchRejectAsync — Applies Single-Proposal Logic Per Item

    [Test]
    public async Task BatchReject_EditedProposalProcessedLikeSingleReject()
    {
        var proposal = new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = _batch.Id,
            ChangeType = ReviewChangeType.AddFact,
            TargetType = ReviewTargetType.ArtifactFact,
            ProposedValueJson = """{"predicate":"location","value":"Black Harbor"}""",
            Confidence = 0.8m,
            Status = ReviewProposalStatus.Edited,
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ReviewedByUserId = _playerUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        await _proposalRepo.CreateAsync(proposal);

        var command = new BatchRejectCommand(
            [proposal.Id], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Succeeded, Contains.Item(proposal.Id));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Rejected));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(_gmUserId));
    }

    [Test]
    public async Task BatchReject_NonExistentProposal_ReportsNotFound()
    {
        var nonExistentId = Guid.NewGuid();
        var command = new BatchRejectCommand(
            [nonExistentId], _worldId, _gmUserId, WorldRole.GM);

        var result = await _service.BatchRejectAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Failed, Has.Count.EqualTo(1));
        Assert.That(result.Value.Failed[0].Code, Is.EqualTo("not_found"));
        Assert.That(result.Value.Failed[0].ProposalId, Is.EqualTo(nonExistentId));
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
