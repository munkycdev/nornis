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
public class ReviewServiceEditTests
{
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private ProposalValidator _validator = null!;
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
        _validator = new ProposalValidator(); // REAL validator — stateless
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
            Status = ReviewBatchStatus.InReview,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };
        _batchRepo.CreateAsync(_batch).GetAwaiter().GetResult();
    }

    #region Happy Path — JSON Replaced, Status → Edited

    [Test]
    public async Task Edit_PendingProposal_ReplacesJsonAndTransitionsToEdited()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss (Edited)","type":"Character","summary":"A suspicious harbor captain"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Edited));
        Assert.That(result.Value.ProposedValueJson, Is.EqualTo(newJson));
        Assert.That(result.Value.ReviewedByUserId, Is.EqualTo(_gmUserId));
        Assert.That(result.Value.ReviewedAt, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task Edit_AlreadyEditedProposal_ReplacesJsonAgain()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Edited);
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss (Re-Edited)","type":"Character"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _playerUserId, WorldRole.Player, newJson);

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(ReviewProposalStatus.Edited));
        Assert.That(result.Value.ProposedValueJson, Is.EqualTo(newJson));
        Assert.That(result.Value.ReviewedByUserId, Is.EqualTo(_playerUserId));
    }

    [Test]
    public async Task Edit_PendingProposal_UpdatesProposalInRepository()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Black Harbor Docks","type":"Location"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        var updated = await _proposalRepo.GetByIdAsync(proposal.Id);
        Assert.That(updated!.ProposedValueJson, Is.EqualTo(newJson));
        Assert.That(updated.Status, Is.EqualTo(ReviewProposalStatus.Edited));
        Assert.That(updated.ReviewedAt, Is.Not.Null);
        Assert.That(updated.ReviewedByUserId, Is.EqualTo(_gmUserId));
    }

    #endregion

    #region No Knowledge Graph Mutation on Edit

    [Test]
    public async Task Edit_DoesNotCreateArtifacts()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Silver Key","type":"Item"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(_artifactRepo.Artifacts, Is.Empty);
    }

    [Test]
    public async Task Edit_DoesNotCreateArtifactFacts()
    {
        var proposal = MakePendingProposal(ReviewChangeType.AddFact);
        proposal.ProposedValueJson = """{"predicate":"location","value":"Black Harbor"}""";
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"predicate":"location","value":"Black Harbor Docks"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(_factRepo.Facts, Is.Empty);
    }

    [Test]
    public async Task Edit_DoesNotCreateArtifactRelationships()
    {
        var artifactAId = Guid.NewGuid();
        var artifactBId = Guid.NewGuid();
        var proposal = MakePendingProposal(ReviewChangeType.AddRelationship);
        proposal.ProposedValueJson = $"{{\"artifactAId\":\"{artifactAId}\",\"artifactBId\":\"{artifactBId}\",\"type\":\"LocatedIn\"}}";
        await _proposalRepo.CreateAsync(proposal);

        var newJson = $"{{\"artifactAId\":\"{artifactAId}\",\"artifactBId\":\"{artifactBId}\",\"type\":\"SuspectedIn\"}}";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(_relationshipRepo.Relationships, Is.Empty);
    }

    [Test]
    public async Task Edit_DoesNotCreateSourceReferences()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss","type":"Character","summary":"Updated summary"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(_sourceRefRepo.References, Is.Empty);
    }

    #endregion

    #region Validation — Invalid JSON or >32768 chars

    [Test]
    public async Task Edit_MalformedJson_ReturnsValidationError()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, "not valid json {{{");

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_payload"));
    }

    [Test]
    public async Task Edit_JsonExceeds32768Characters_ReturnsValidationError()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var oversizedJson = "{\"name\":\"" + new string('A', 32_769) + "\",\"type\":\"Character\"}";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, oversizedJson);

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("payload_too_large"));
    }

    [Test]
    public async Task Edit_EmptyJson_ReturnsValidationError()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, "");

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_payload"));
    }

    [Test]
    public async Task Edit_CreateArtifactPayloadMissingName_ReturnsValidationError()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var invalidJson = """{"type":"Character","summary":"No name provided"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, invalidJson);

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_payload"));
    }

    #endregion

    #region Accepted/Rejected Proposals Cannot Be Edited (409)

    [Test]
    public async Task Edit_AcceptedProposal_Returns409Conflict()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Accepted);
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss","type":"Character"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("conflict"));
    }

    [Test]
    public async Task Edit_RejectedProposal_Returns409Conflict()
    {
        var proposal = MakeProposalWithStatus(ReviewProposalStatus.Rejected);
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss","type":"Character"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        var result = await _service.EditProposalAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("conflict"));
    }

    #endregion

    #region Edited Proposal Subsequently Accepted Applies Edited JSON

    [Test]
    public async Task Edit_ThenAccept_AppliesEditedJson()
    {
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        // Edit the proposal
        var editedJson = """{"name":"Captain Voss (Corrected)","type":"Character","summary":"Corrected by reviewer"}""";
        var editCommand = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, editedJson);

        var editResult = await _service.EditProposalAsync(editCommand, CancellationToken.None);
        Assert.That(editResult.IsSuccess, Is.True);

        // Accept the edited proposal
        var acceptCommand = new AcceptProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM);

        var acceptResult = await _service.AcceptProposalAsync(acceptCommand, CancellationToken.None);

        Assert.That(acceptResult.IsSuccess, Is.True);
        Assert.That(acceptResult.Value!.Status, Is.EqualTo(ReviewProposalStatus.Accepted));

        // Verify the proposal's ProposedValueJson is the edited value
        var updatedProposal = await _proposalRepo.GetByIdAsync(proposal.Id);
        Assert.That(updatedProposal!.ProposedValueJson, Is.EqualTo(editedJson));
        Assert.That(updatedProposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
    }

    #endregion

    #region Batch Transitions on First Edit

    [Test]
    public async Task Edit_FirstEditInPendingBatch_TransitionsBatchToInReview()
    {
        var pendingBatch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            SourceId = _source.Id,
            Status = ReviewBatchStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
        await _batchRepo.CreateAsync(pendingBatch);

        var proposal = MakePendingProposal(batchId: pendingBatch.Id);
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss","type":"Character","summary":"Edited summary"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(pendingBatch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.InReview));
    }

    [Test]
    public async Task Edit_BatchAlreadyInReview_RemainsInReview()
    {
        // Default _batch is already InReview
        var proposal = MakePendingProposal();
        await _proposalRepo.CreateAsync(proposal);

        var newJson = """{"name":"Captain Voss","type":"Character"}""";
        var command = new EditProposalCommand(
            proposal.Id, _worldId, _gmUserId, WorldRole.GM, newJson);

        await _service.EditProposalAsync(command, CancellationToken.None);

        var updatedBatch = await _batchRepo.GetByIdAsync(_batch.Id);
        Assert.That(updatedBatch!.Status, Is.EqualTo(ReviewBatchStatus.InReview));
    }

    #endregion

    #region Helpers

    private ReviewProposal MakePendingProposal(
        ReviewChangeType changeType = ReviewChangeType.CreateArtifact,
        Guid? batchId = null)
    {
        return new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId ?? _batch.Id,
            ChangeType = changeType,
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
