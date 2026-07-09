using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ReviewService : IReviewService
{
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProposalValidator _proposalValidator;
    private readonly IProposalApplicator _proposalApplicator;

    public ReviewService(
        IReviewProposalRepository reviewProposalRepository,
        IReviewBatchRepository reviewBatchRepository,
        ISourceRepository sourceRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IUnitOfWork unitOfWork,
        IProposalValidator proposalValidator,
        IProposalApplicator proposalApplicator)
    {
        _reviewProposalRepository = reviewProposalRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _sourceRepository = sourceRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _unitOfWork = unitOfWork;
        _proposalValidator = proposalValidator;
        _proposalApplicator = proposalApplicator;
    }

    public async Task<AppResult<ReviewQueueResult>> ListReviewQueueAsync(
        ReviewQueueQuery query, CancellationToken ct)
    {
        // Observer sees nothing
        if (query.ActingUserRole == WorldRole.Observer)
            return AppResult<ReviewQueueResult>.Success(new ReviewQueueResult([], false));

        // Validate FilterByBatchId if provided
        if (query.FilterByBatchId is not null)
        {
            var filterBatch = await _reviewBatchRepository.GetByIdAsync(query.FilterByBatchId.Value, ct);
            if (filterBatch is null || filterBatch.WorldId != query.WorldId)
                return AppResult<ReviewQueueResult>.Fail(new AppError(404, "not_found", "Review batch not found."));
        }

        // Compute allowed source IDs based on role and visibility
        var sources = await _sourceRepository.ListByWorldAsync(query.WorldId, cancellationToken: ct);
        var allowedSourceIds = GetAllowedSourceIds(sources, query.ActingUserId, query.ActingUserRole);

        if (allowedSourceIds.Count == 0)
            return AppResult<ReviewQueueResult>.Success(new ReviewQueueResult([], false));

        var (proposals, hasMore) = await _reviewProposalRepository.ListReviewQueueAsync(
            query.WorldId, allowedSourceIds, query.FilterByBatchId, limit: 200, ct);

        var context = await BuildProposalContextAsync(query.WorldId, proposals, sources, ct);

        return AppResult<ReviewQueueResult>.Success(new ReviewQueueResult(proposals, hasMore, context));
    }

    /// <summary>
    /// Resolves display context per proposal: the source that produced it (via its batch) and a
    /// human-readable name for what it targets, so the review UI never shows a bare GUID.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, ReviewProposalContext>> BuildProposalContextAsync(
        Guid worldId,
        IReadOnlyList<ReviewProposal> proposals,
        IReadOnlyList<Source> sources,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, ReviewProposalContext>();
        if (proposals.Count == 0)
            return result;

        var batches = (await _reviewBatchRepository.ListByWorldAsync(worldId, ct))
            .ToDictionary(b => b.Id);
        var sourceTitles = sources.ToDictionary(s => s.Id, s => s.Title);

        var artifacts = await _artifactRepository.ListByWorldAsync(worldId, null, null, ct);
        var artifactNames = artifacts.ToDictionary(a => a.Id, a => a.Name);

        // Facts and relationships are only needed for Update* proposals — load lazily.
        Dictionary<Guid, ArtifactFact>? factsById = null;
        Dictionary<Guid, ArtifactRelationship>? relationshipsById = null;

        if (proposals.Any(p => p.ChangeType == ReviewChangeType.UpdateFact && p.TargetId is not null))
        {
            var facts = await _artifactFactRepository.ListByArtifactIdsAsync(
                artifactNames.Keys.ToList(), int.MaxValue, ct);
            factsById = facts.ToDictionary(f => f.Id);
        }

        if (proposals.Any(p => p.ChangeType == ReviewChangeType.UpdateRelationship && p.TargetId is not null))
        {
            var relationships = await _artifactRelationshipRepository.ListByArtifactIdsAsync(
                artifactNames.Keys.ToList(),
                [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private], ct);
            relationshipsById = relationships.ToDictionary(r => r.Id);
        }

        foreach (var proposal in proposals)
        {
            Guid sourceId = default;
            var sourceTitle = "Unknown source";
            if (batches.TryGetValue(proposal.ReviewBatchId, out var batch))
            {
                sourceId = batch.SourceId;
                sourceTitle = sourceTitles.GetValueOrDefault(batch.SourceId, sourceTitle);
            }

            var targetName = ResolveTargetName(proposal, artifactNames, factsById, relationshipsById);

            string? mergeSourceName = null;
            if (proposal.ChangeType == ReviewChangeType.MergeArtifact)
            {
                var payload = DeserializePayload<MergeArtifactPayload>(proposal.ProposedValueJson);
                if (payload is not null && artifactNames.TryGetValue(payload.SourceArtifactId, out var name))
                    mergeSourceName = name;
            }

            result[proposal.Id] = new ReviewProposalContext(sourceId, sourceTitle, targetName, mergeSourceName);
        }

        return result;
    }

    private static string? ResolveTargetName(
        ReviewProposal proposal,
        Dictionary<Guid, string> artifactNames,
        Dictionary<Guid, ArtifactFact>? factsById,
        Dictionary<Guid, ArtifactRelationship>? relationshipsById)
    {
        switch (proposal.ChangeType)
        {
            // AddFact's TargetId references the owning artifact; when it is null the payload
            // carries a same-batch artifactName instead.
            case ReviewChangeType.AddFact:
                if (proposal.TargetId is { } factArtifactId)
                    return artifactNames.GetValueOrDefault(factArtifactId);
                return DeserializePayload<AddFactPayload>(proposal.ProposedValueJson)?.ArtifactName;

            case ReviewChangeType.UpdateArtifact:
            case ReviewChangeType.MergeArtifact:
                return proposal.TargetId is { } artifactId
                    ? artifactNames.GetValueOrDefault(artifactId)
                    : null;

            case ReviewChangeType.UpdateFact:
                if (proposal.TargetId is { } factId && factsById?.GetValueOrDefault(factId) is { } fact)
                {
                    var owner = artifactNames.GetValueOrDefault(fact.ArtifactId, "Unknown artifact");
                    return $"{owner} — {fact.Predicate}";
                }
                return null;

            case ReviewChangeType.AddRelationship:
            {
                var payload = DeserializePayload<AddRelationshipPayload>(proposal.ProposedValueJson);
                if (payload is null)
                    return null;
                var a = payload.ArtifactAId is { } aId
                    ? artifactNames.GetValueOrDefault(aId, payload.ArtifactAName ?? "?")
                    : payload.ArtifactAName ?? "?";
                var b = payload.ArtifactBId is { } bId
                    ? artifactNames.GetValueOrDefault(bId, payload.ArtifactBName ?? "?")
                    : payload.ArtifactBName ?? "?";
                return $"{a} ↔ {b}";
            }

            case ReviewChangeType.UpdateRelationship:
                if (proposal.TargetId is { } relId && relationshipsById?.GetValueOrDefault(relId) is { } rel)
                {
                    var a = artifactNames.GetValueOrDefault(rel.ArtifactAId, "?");
                    var b = artifactNames.GetValueOrDefault(rel.ArtifactBId, "?");
                    return $"{a} ↔ {b}";
                }
                return null;

            default:
                return null; // CreateArtifact: the payload's own name is already displayed
        }
    }

    private static T? DeserializePayload<T>(string json) where T : class
    {
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json, PayloadJsonOptions);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppResult<AcceptProposalResult>> AcceptProposalAsync(
        AcceptProposalCommand command, CancellationToken ct)
    {
        var proposal = await _reviewProposalRepository.GetByIdAsync(command.ProposalId, ct);
        if (proposal is null)
            return AppResult<AcceptProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        var batch = await _reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId, ct);
        if (batch is null || batch.WorldId != command.WorldId)
            return AppResult<AcceptProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<AcceptProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        if (!IsSourceVisibleToUser(source, command.ActingUserId, command.ActingUserRole))
            return AppResult<AcceptProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        var authResult = CheckReviewAuthorization(command.ActingUserRole, command.ActingUserId, source);
        if (!authResult.IsSuccess)
            return AppResult<AcceptProposalResult>.Fail(authResult.Error!);

        // Idempotent: already Accepted
        if (proposal.Status == ReviewProposalStatus.Accepted)
            return AppResult<AcceptProposalResult>.Success(new AcceptProposalResult(
                proposal.Id, proposal.Status, proposal.ReviewedAt!.Value, proposal.ReviewedByUserId!.Value, proposal.TargetId));

        // Conflicting: Rejected → 409
        if (proposal.Status == ReviewProposalStatus.Rejected)
            return AppResult<AcceptProposalResult>.Fail(new AppError(409, "conflict", "Cannot accept a proposal that has already been rejected."));

        // Guard: only Pending or Edited
        if (proposal.Status is not (ReviewProposalStatus.Pending or ReviewProposalStatus.Edited))
            return AppResult<AcceptProposalResult>.Fail(new AppError(409, "invalid_status", "Only Pending or Edited proposals can be accepted."));

        // Validate ProposedValueJson
        var validationResult = _proposalValidator.ValidateProposedValue(proposal.ProposedValueJson, proposal.ChangeType);
        if (!validationResult.IsSuccess)
            return AppResult<AcceptProposalResult>.Fail(validationResult.Error!);

        // Begin transaction
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, ct);
            if (!applyResult.IsSuccess)
            {
                await transaction.RollbackAsync(ct);
                return AppResult<AcceptProposalResult>.Fail(applyResult.Error!);
            }

            var now = DateTimeOffset.UtcNow;
            proposal.Status = ReviewProposalStatus.Accepted;
            proposal.ReviewedAt = now;
            proposal.ReviewedByUserId = command.ActingUserId;
            await _reviewProposalRepository.UpdateAsync(proposal, ct);

            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            return AppResult<AcceptProposalResult>.Fail(
                new AppError(500, "transaction_failed", $"Failed to accept proposal {proposal.Id}. The operation could not be completed."));
        }

        // Batch lifecycle OUTSIDE transaction
        await UpdateBatchLifecycleAsync(batch.Id, ct);

        return AppResult<AcceptProposalResult>.Success(new AcceptProposalResult(
            proposal.Id, proposal.Status, proposal.ReviewedAt!.Value, proposal.ReviewedByUserId!.Value, proposal.TargetId));
    }

    public async Task<AppResult<RejectProposalResult>> RejectProposalAsync(
        RejectProposalCommand command, CancellationToken ct)
    {
        var proposal = await _reviewProposalRepository.GetByIdAsync(command.ProposalId, ct);
        if (proposal is null)
            return AppResult<RejectProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        var batch = await _reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId, ct);
        if (batch is null || batch.WorldId != command.WorldId)
            return AppResult<RejectProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
            return AppResult<RejectProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        if (!IsSourceVisibleToUser(source, command.ActingUserId, command.ActingUserRole))
            return AppResult<RejectProposalResult>.Fail(new AppError(404, "not_found", "Proposal not found."));

        var authResult = CheckReviewAuthorization(command.ActingUserRole, command.ActingUserId, source);
        if (!authResult.IsSuccess)
            return AppResult<RejectProposalResult>.Fail(authResult.Error!);

        // Idempotent: already Rejected
        if (proposal.Status == ReviewProposalStatus.Rejected)
            return AppResult<RejectProposalResult>.Success(new RejectProposalResult(
                proposal.Id, proposal.Status, proposal.ReviewedAt!.Value, proposal.ReviewedByUserId!.Value));

        // Conflicting: Accepted → 409
        if (proposal.Status == ReviewProposalStatus.Accepted)
            return AppResult<RejectProposalResult>.Fail(new AppError(409, "conflict", "Cannot reject a proposal that has already been accepted."));

        // Guard: only Pending or Edited
        if (proposal.Status is not (ReviewProposalStatus.Pending or ReviewProposalStatus.Edited))
            return AppResult<RejectProposalResult>.Fail(new AppError(409, "invalid_status", "Only Pending or Edited proposals can be rejected."));

        var now = DateTimeOffset.UtcNow;
        proposal.Status = ReviewProposalStatus.Rejected;
        proposal.ReviewedAt = now;
        proposal.ReviewedByUserId = command.ActingUserId;
        await _reviewProposalRepository.UpdateAsync(proposal, ct);

        await UpdateBatchLifecycleAsync(batch.Id, ct);

        return AppResult<RejectProposalResult>.Success(new RejectProposalResult(
            proposal.Id, proposal.Status, now, command.ActingUserId));
    }

    public async Task<AppResult<EditProposalResult>> EditProposalAsync(
        EditProposalCommand command, CancellationToken ct)
    {
        // 1. Load proposal by Id
        var proposal = await _reviewProposalRepository.GetByIdAsync(command.ProposalId, ct);
        if (proposal is null)
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(404, "not_found", "Proposal not found."));
        }

        // 2. Load batch by proposal.ReviewBatchId
        var batch = await _reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId, ct);
        if (batch is null)
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(500, "internal_error", "Review batch not found for proposal."));
        }

        // 3. Load source by batch.SourceId
        var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
        if (source is null)
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(500, "internal_error", "Source not found for review batch."));
        }

        // 4. Check visibility: if source is invisible to the user, return not-found
        if (!IsSourceVisibleToUser(source, command.ActingUserId, command.ActingUserRole))
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(404, "not_found", "Proposal not found."));
        }

        // 5. Check authorization: Observer → 403, Player not owning source → 403
        var authResult = CheckReviewAuthorization(command.ActingUserRole, command.ActingUserId, source);
        if (!authResult.IsSuccess)
        {
            return AppResult<EditProposalResult>.Fail(authResult.Error!);
        }

        // 6. If proposal.Status is Accepted or Rejected → return 409 conflict error
        if (proposal.Status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected)
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(409, "conflict", "Only Pending or Edited proposals can be edited."));
        }

        // 7. If proposal.Status is not Pending and not Edited → error
        if (proposal.Status is not ReviewProposalStatus.Pending and not ReviewProposalStatus.Edited)
        {
            return AppResult<EditProposalResult>.Fail(
                new AppError(400, "invalid_status", "Only Pending or Edited proposals can be edited."));
        }

        // 8. Validate the NEW ProposedValueJson via IProposalValidator
        var validationResult = _proposalValidator.ValidateProposedValue(
            command.NewProposedValueJson, proposal.ChangeType);
        if (!validationResult.IsSuccess)
        {
            return AppResult<EditProposalResult>.Fail(validationResult.Error!);
        }

        // 9. Replace ProposedValueJson on proposal
        proposal.ProposedValueJson = command.NewProposedValueJson;

        // 10. Update proposal: Status=Edited, ReviewedAt=UtcNow, ReviewedByUserId
        var now = DateTimeOffset.UtcNow;
        proposal.Status = ReviewProposalStatus.Edited;
        proposal.ReviewedAt = now;
        proposal.ReviewedByUserId = command.ActingUserId;

        // 11. Save proposal via UpdateAsync
        await _reviewProposalRepository.UpdateAsync(proposal, ct);

        // 12. Update batch lifecycle (Pending→InReview on first edit)
        await UpdateBatchLifecycleAsync(proposal.ReviewBatchId, ct);

        // 13. Return EditProposalResult
        return AppResult<EditProposalResult>.Success(
            new EditProposalResult(
                proposal.Id,
                ReviewProposalStatus.Edited,
                proposal.ProposedValueJson,
                now,
                command.ActingUserId));
    }

    public async Task<AppResult<BatchOperationResult>> BatchAcceptAsync(
        BatchAcceptCommand command, CancellationToken ct)
    {
        if (command.ProposalIds.Count < 1 || command.ProposalIds.Count > 50)
            return AppResult<BatchOperationResult>.Fail(
                new AppError(400, "validation_error", "Batch size must be between 1 and 50 proposals."));

        var uniqueIds = command.ProposalIds.Distinct().ToList();
        var succeeded = new List<Guid>();
        var failed = new List<BatchFailureDetail>();
        var affectedBatchIds = new HashSet<Guid>();

        foreach (var proposalId in uniqueIds)
        {
            var proposal = await _reviewProposalRepository.GetByIdAsync(proposalId, ct);
            if (proposal is null)
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            var batch = await _reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId, ct);
            if (batch is null || batch.WorldId != command.WorldId)
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
            if (source is null)
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            if (!IsSourceVisibleToUser(source, command.ActingUserId, command.ActingUserRole))
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            var authResult = CheckReviewAuthorization(command.ActingUserRole, command.ActingUserId, source);
            if (!authResult.IsSuccess)
            {
                failed.Add(new BatchFailureDetail(proposalId, "forbidden", authResult.Error!.Message));
                continue;
            }

            // Idempotent: already Accepted
            if (proposal.Status == ReviewProposalStatus.Accepted)
            {
                succeeded.Add(proposalId);
                affectedBatchIds.Add(batch.Id);
                continue;
            }

            // Conflicting: Rejected → conflict
            if (proposal.Status == ReviewProposalStatus.Rejected)
            {
                failed.Add(new BatchFailureDetail(proposalId, "conflict", "Cannot accept a proposal that has already been rejected."));
                continue;
            }

            // Guard: only Pending or Edited
            if (proposal.Status is not (ReviewProposalStatus.Pending or ReviewProposalStatus.Edited))
            {
                failed.Add(new BatchFailureDetail(proposalId, "conflict", "Only Pending or Edited proposals can be accepted."));
                continue;
            }

            // Validate ProposedValueJson
            var validationResult = _proposalValidator.ValidateProposedValue(proposal.ProposedValueJson, proposal.ChangeType);
            if (!validationResult.IsSuccess)
            {
                failed.Add(new BatchFailureDetail(proposalId, validationResult.Error!.Code, validationResult.Error!.Message));
                continue;
            }

            // Begin transaction per proposal
            await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
            try
            {
                var applyResult = await _proposalApplicator.ApplyAsync(proposal, batch, ct);
                if (!applyResult.IsSuccess)
                {
                    await transaction.RollbackAsync(ct);
                    failed.Add(new BatchFailureDetail(proposalId, applyResult.Error!.Code, applyResult.Error!.Message));
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                proposal.Status = ReviewProposalStatus.Accepted;
                proposal.ReviewedAt = now;
                proposal.ReviewedByUserId = command.ActingUserId;
                await _reviewProposalRepository.UpdateAsync(proposal, ct);

                await transaction.CommitAsync(ct);
                succeeded.Add(proposalId);
                affectedBatchIds.Add(batch.Id);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                failed.Add(new BatchFailureDetail(proposalId, "transaction_failed", $"Failed to accept proposal {proposalId}. The operation could not be completed."));
            }
        }

        // Batch lifecycle OUTSIDE individual transactions
        foreach (var batchId in affectedBatchIds)
        {
            await UpdateBatchLifecycleAsync(batchId, ct);
        }

        return AppResult<BatchOperationResult>.Success(new BatchOperationResult(succeeded, failed));
    }

    public async Task<AppResult<BatchOperationResult>> BatchRejectAsync(
        BatchRejectCommand command, CancellationToken ct)
    {
        if (command.ProposalIds.Count < 1 || command.ProposalIds.Count > 50)
            return AppResult<BatchOperationResult>.Fail(
                new AppError(400, "validation_error", "Batch size must be between 1 and 50 proposals."));

        var uniqueIds = command.ProposalIds.Distinct().ToList();
        var succeeded = new List<Guid>();
        var failed = new List<BatchFailureDetail>();
        var affectedBatchIds = new HashSet<Guid>();

        foreach (var proposalId in uniqueIds)
        {
            var proposal = await _reviewProposalRepository.GetByIdAsync(proposalId, ct);
            if (proposal is null)
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            var batch = await _reviewBatchRepository.GetByIdAsync(proposal.ReviewBatchId, ct);
            if (batch is null || batch.WorldId != command.WorldId)
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            var source = await _sourceRepository.GetByIdAsync(batch.SourceId, ct);
            if (source is null)
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            if (!IsSourceVisibleToUser(source, command.ActingUserId, command.ActingUserRole))
            {
                failed.Add(new BatchFailureDetail(proposalId, "not_found", "Proposal not found."));
                continue;
            }

            var authResult = CheckReviewAuthorization(command.ActingUserRole, command.ActingUserId, source);
            if (!authResult.IsSuccess)
            {
                failed.Add(new BatchFailureDetail(proposalId, "forbidden", authResult.Error!.Message));
                continue;
            }

            // Idempotent: already Rejected
            if (proposal.Status == ReviewProposalStatus.Rejected)
            {
                succeeded.Add(proposalId);
                affectedBatchIds.Add(batch.Id);
                continue;
            }

            // Conflicting: Accepted → conflict
            if (proposal.Status == ReviewProposalStatus.Accepted)
            {
                failed.Add(new BatchFailureDetail(proposalId, "conflict", "Cannot reject a proposal that has already been accepted."));
                continue;
            }

            // Guard: only Pending or Edited
            if (proposal.Status is not (ReviewProposalStatus.Pending or ReviewProposalStatus.Edited))
            {
                failed.Add(new BatchFailureDetail(proposalId, "conflict", "Only Pending or Edited proposals can be rejected."));
                continue;
            }

            var now = DateTimeOffset.UtcNow;
            proposal.Status = ReviewProposalStatus.Rejected;
            proposal.ReviewedAt = now;
            proposal.ReviewedByUserId = command.ActingUserId;
            await _reviewProposalRepository.UpdateAsync(proposal, ct);

            succeeded.Add(proposalId);
            affectedBatchIds.Add(batch.Id);
        }

        // Batch lifecycle OUTSIDE individual proposal processing
        foreach (var batchId in affectedBatchIds)
        {
            await UpdateBatchLifecycleAsync(batchId, ct);
        }

        return AppResult<BatchOperationResult>.Success(new BatchOperationResult(succeeded, failed));
    }

    private static IReadOnlyList<Guid> GetAllowedSourceIds(
        IReadOnlyList<Source> worldSources, Guid userId, WorldRole role)
    {
        return role switch
        {
            WorldRole.GM => worldSources.Select(s => s.Id).ToList(),
            WorldRole.Player => worldSources
                .Where(s => s.CreatedByUserId == userId)
                .Select(s => s.Id).ToList(),
            WorldRole.Observer => [],
            _ => []
        };
    }

    private bool IsSourceVisibleToUser(Source source, Guid userId, WorldRole role)
    {
        return role switch
        {
            WorldRole.GM => true,
            WorldRole.Player => source.CreatedByUserId == userId,
            WorldRole.Observer => false,
            _ => false
        };
    }

    private static AppResult CheckReviewAuthorization(WorldRole role, Guid actingUserId, Source source)
    {
        return role switch
        {
            WorldRole.Observer => AppResult.Fail(
                new AppError(403, "forbidden", "Observers cannot review proposals.")),
            WorldRole.Player when source.CreatedByUserId != actingUserId => AppResult.Fail(
                new AppError(403, "forbidden", "Players can only review proposals from their own sources.")),
            _ => AppResult.Success()
        };
    }

    private async Task UpdateBatchLifecycleAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _reviewBatchRepository.GetByIdAsync(batchId, ct);
        if (batch is null) return;

        // Don't touch Canceled or Failed batches
        if (batch.Status is ReviewBatchStatus.Canceled or ReviewBatchStatus.Failed)
            return;

        var proposals = await _reviewProposalRepository.ListByReviewBatchAsync(batchId, ct);

        if (batch.Status == ReviewBatchStatus.Pending)
        {
            // First review transitions to InReview
            await _reviewBatchRepository.UpdateStatusAsync(batchId, ReviewBatchStatus.InReview, ct);
            batch.Status = ReviewBatchStatus.InReview;
        }

        // Check if all proposals are in terminal state
        var allTerminal = proposals.All(p =>
            p.Status is ReviewProposalStatus.Accepted or ReviewProposalStatus.Rejected);

        if (allTerminal && proposals.Count > 0 &&
            batch.Status == ReviewBatchStatus.InReview)
        {
            await _reviewBatchRepository.UpdateCompletedAsync(
                batchId, DateTimeOffset.UtcNow, ct);
        }
    }
}
