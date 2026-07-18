using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Edit-and-reprocess for extracted sources. The old body's derived knowledge is torn
/// down before requeueing, but only where it is solely attributable to this source:
/// facts and relationships this source created are deleted; artifacts it created are
/// deleted only when nothing else has built on them (no remaining facts or
/// relationships, no references from other sources, no character links). Entities this
/// source merely updated, and shared artifacts, are left intact — reprocessing will
/// propose against them again by name match.
/// </summary>
public class SourceReprocessService : ISourceReprocessService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ICharacterRepository _characterRepository;
    private readonly IExtractionQueueClient _extractionQueueClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SourceReprocessService> _logger;

    public SourceReprocessService(
        ISourceRepository sourceRepository,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ICharacterRepository characterRepository,
        IExtractionQueueClient extractionQueueClient,
        IUnitOfWork unitOfWork,
        ILogger<SourceReprocessService> logger)
    {
        _sourceRepository = sourceRepository;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _characterRepository = characterRepository;
        _extractionQueueClient = extractionQueueClient;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AppResult<ReprocessPreview>> PreviewAsync(
        Guid sourceId, Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var sourceResult = await AuthorizeAsync(sourceId, worldId, actingUserId, actingUserRole, ct);
        if (!sourceResult.IsSuccess)
            return AppResult<ReprocessPreview>.Fail(sourceResult.Error!);

        var plan = await ComputeCascadePlanAsync(sourceId, worldId, ct);

        return AppResult<ReprocessPreview>.Success(new ReprocessPreview(
            ArtifactNamesToDelete: plan.ArtifactsToDelete.Select(a => a.Name).ToList(),
            ArtifactNamesToKeep: plan.ArtifactsToKeep.Select(a => a.Name).ToList(),
            FactsToDelete: plan.FactIdsToDelete.Count,
            RelationshipsToDelete: plan.RelationshipIdsToDelete.Count,
            PendingProposalsToDiscard: plan.PendingProposalCount));
    }

    public async Task<AppResult<Source>> ReprocessAsync(ReprocessSourceCommand command, CancellationToken ct)
    {
        var sourceResult = await AuthorizeAsync(
            command.SourceId, command.WorldId, command.ActingUserId, command.ActingUserRole, ct);
        if (!sourceResult.IsSuccess)
            return AppResult<Source>.Fail(sourceResult.Error!);

        var source = sourceResult.Value!;

        // Validate edits before any destructive work.
        if (command.Title is not null)
        {
            var titleError = SourceService.ValidateTitle(command.Title);
            if (titleError is not null)
                return AppResult<Source>.Fail(titleError);
        }

        if (command.Body is not null)
        {
            var bodyError = SourceService.ValidateBody(command.Body);
            if (bodyError is not null)
                return AppResult<Source>.Fail(bodyError);
        }

        if (command.Uri is not null)
        {
            var uriError = SourceService.ValidateUri(command.Uri);
            if (uriError is not null)
                return AppResult<Source>.Fail(uriError);
        }

        var plan = await ComputeCascadePlanAsync(command.SourceId, command.WorldId, ct);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        try
        {
            foreach (var factId in plan.FactIdsToDelete)
            {
                await _artifactFactRepository.DeleteAsync(factId, ct);
            }

            foreach (var relationshipId in plan.RelationshipIdsToDelete)
            {
                await _artifactRelationshipRepository.DeleteAsync(relationshipId, ct);
            }

            foreach (var artifact in plan.ArtifactsToDelete)
            {
                await _artifactRepository.DeleteAsync(artifact.Id, ct);
            }

            // The old body's provenance trail no longer applies — including references to
            // kept entities, whose quotes cite text that may no longer exist.
            await _sourceReferenceRepository.DeleteBySourceAsync(command.SourceId, ct);

            // Deleting the batches (pending proposals cascade with them) is what unlocks
            // re-extraction: the worker's idempotency check keys on batch existence.
            await _reviewBatchRepository.DeleteBySourceAsync(command.SourceId, ct);

            if (command.Title is not null)
                source.Title = command.Title;
            if (command.Body is not null)
                source.Body = command.Body;
            if (command.Uri is not null)
                source.Uri = command.Uri;
            if (command.OccurredAt is not null)
                source.OccurredAt = command.OccurredAt;

            // Commit Queued BEFORE enqueueing (same invariant as MarkReadyAsync): the
            // worker skips any message whose source is not Queued.
            source.ProcessingStatus = SourceProcessingStatus.Queued;
            source = await _sourceRepository.UpdateAsync(source, ct);

            await transaction.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(ct);
            _logger.LogError(ex,
                "Reprocess cascade failed. SourceId={SourceId}, WorldId={WorldId}",
                command.SourceId, command.WorldId);
            return AppResult<Source>.Fail(new AppError(500, "transaction_failed",
                "Failed to reprocess the source. No changes were made."));
        }

        _logger.LogInformation(
            "Source reprocess cascade complete. SourceId={SourceId}, ArtifactsDeleted={ArtifactsDeleted}, " +
            "ArtifactsKept={ArtifactsKept}, FactsDeleted={FactsDeleted}, RelationshipsDeleted={RelationshipsDeleted}",
            command.SourceId, plan.ArtifactsToDelete.Count, plan.ArtifactsToKeep.Count,
            plan.FactIdsToDelete.Count, plan.RelationshipIdsToDelete.Count);

        try
        {
            await _extractionQueueClient.SendExtractionMessageAsync(source.Id, source.WorldId, ct);
        }
        catch (Exception ex)
        {
            // The cascade is committed; only the requeue failed. Failed gives the user
            // the existing retry path (Failed → Ready → Queued via mark-ready).
            _logger.LogError(ex,
                "Failed to enqueue reprocess extraction. SourceId={SourceId}", source.Id);
            source.ProcessingStatus = SourceProcessingStatus.Failed;
            source = await _sourceRepository.UpdateAsync(source, ct);
            return AppResult<Source>.Fail(new AppError(502, "enqueue_failed",
                "The edits were saved and old knowledge was removed, but queueing extraction failed. Retry processing from the source page."));
        }

        return AppResult<Source>.Success(source);
    }

    /// <summary>
    /// Shared gate for preview and reprocess: source exists in this world, actor is the
    /// creator or a GM, and the source is in a reprocessable state (Processed or Failed —
    /// in-flight sources are locked, unprocessed ones have nothing to cascade).
    /// </summary>
    private async Task<AppResult<Source>> AuthorizeAsync(
        Guid sourceId, Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        if (actingUserRole == WorldRole.Observer)
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot reprocess sources."));

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null || source.WorldId != worldId)
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));

        if (source.CreatedByUserId != actingUserId && actingUserRole != WorldRole.GM)
            return AppResult<Source>.Fail(new AppError(403, "forbidden",
                "Only the source creator or a GM can reprocess this source."));

        if (source.ProcessingStatus is not (SourceProcessingStatus.Processed or SourceProcessingStatus.Failed))
            return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                $"Only Processed or Failed sources can be reprocessed; this source is {source.ProcessingStatus}."));

        return AppResult<Source>.Success(source);
    }

    private sealed record CascadePlan(
        IReadOnlyList<Guid> FactIdsToDelete,
        IReadOnlyList<Guid> RelationshipIdsToDelete,
        IReadOnlyList<Artifact> ArtifactsToDelete,
        IReadOnlyList<Artifact> ArtifactsToKeep,
        int PendingProposalCount);

    private async Task<CascadePlan> ComputeCascadePlanAsync(Guid sourceId, Guid worldId, CancellationToken ct)
    {
        // The proposal trail distinguishes created from updated: an accepted UpdateFact /
        // UpdateRelationship targets an entity that existed before this source touched it.
        var batches = await _reviewBatchRepository.ListBySourceAsync(sourceId, ct);
        var acceptedProposals = new List<ReviewProposal>();
        var pendingCount = 0;

        foreach (var batch in batches)
        {
            foreach (var proposal in await _reviewProposalRepository.ListByReviewBatchAsync(batch.Id, ct))
            {
                if (proposal.Status == ReviewProposalStatus.Accepted)
                    acceptedProposals.Add(proposal);
                else if (proposal.Status is ReviewProposalStatus.Pending or ReviewProposalStatus.Edited)
                    pendingCount++;
            }
        }

        var createdArtifactIds = acceptedProposals
            .Where(p => p.ChangeType == ReviewChangeType.CreateArtifact && p.TargetId is not null)
            .Select(p => p.TargetId!.Value)
            .ToHashSet();

        var updatedFactIds = acceptedProposals
            .Where(p => p.ChangeType == ReviewChangeType.UpdateFact && p.TargetId is not null)
            .Select(p => p.TargetId!.Value)
            .ToHashSet();

        var updatedRelationshipIds = acceptedProposals
            .Where(p => p.ChangeType == ReviewChangeType.UpdateRelationship && p.TargetId is not null)
            .Select(p => p.TargetId!.Value)
            .ToHashSet();

        var references = await _sourceReferenceRepository.ListBySourceAsync(sourceId, ct);

        var factIdsToDelete = references
            .Where(r => r.TargetType == SourceReferenceTargetType.ArtifactFact)
            .Select(r => r.TargetId)
            .Where(id => !updatedFactIds.Contains(id))
            .Distinct()
            .ToList();

        var relationshipIdsToDelete = references
            .Where(r => r.TargetType == SourceReferenceTargetType.ArtifactRelationship)
            .Select(r => r.TargetId)
            .Where(id => !updatedRelationshipIds.Contains(id))
            .Distinct()
            .ToList();

        var factDeleteSet = factIdsToDelete.ToHashSet();
        var relationshipDeleteSet = relationshipIdsToDelete.ToHashSet();

        var characterLinkedArtifactIds = (await _characterRepository.ListByWorldAsync(worldId, ct))
            .Where(c => c.ArtifactId is not null)
            .Select(c => c.ArtifactId!.Value)
            .ToHashSet();

        var artifactsToDelete = new List<Artifact>();
        var artifactsToKeep = new List<Artifact>();

        foreach (var artifactId in createdArtifactIds)
        {
            var artifact = await _artifactRepository.GetByIdAsync(artifactId, ct);
            if (artifact is null)
                continue; // already gone (e.g. merged away and hard-deleted)

            var hasRemainingFacts = (await _artifactFactRepository.ListByArtifactAsync(artifactId, ct))
                .Any(f => !factDeleteSet.Contains(f.Id));

            var hasRemainingRelationships = (await _artifactRelationshipRepository.ListByArtifactAsync(artifactId, ct))
                .Any(r => !relationshipDeleteSet.Contains(r.Id));

            var hasOtherSourceReferences = (await _sourceReferenceRepository.ListByTargetAsync(
                    SourceReferenceTargetType.Artifact, artifactId, ct))
                .Any(r => r.SourceId != sourceId);

            var isOrphaned = !hasRemainingFacts
                && !hasRemainingRelationships
                && !hasOtherSourceReferences
                && !characterLinkedArtifactIds.Contains(artifactId);

            if (isOrphaned)
                artifactsToDelete.Add(artifact);
            else
                artifactsToKeep.Add(artifact);
        }

        return new CascadePlan(factIdsToDelete, relationshipIdsToDelete, artifactsToDelete, artifactsToKeep, pendingCount);
    }
}
