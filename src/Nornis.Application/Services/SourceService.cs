using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class SourceService : ISourceService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly ICampaignRepository _campaignRepository;
    private readonly IExtractionQueueClient _extractionQueueClient;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<SourceService> _logger;

    private static readonly Dictionary<SourceProcessingStatus, HashSet<SourceProcessingStatus>> ValidTransitions = new()
    {
        [SourceProcessingStatus.Draft] = new() { SourceProcessingStatus.Ready },
        [SourceProcessingStatus.Ready] = new() { SourceProcessingStatus.Queued },
        [SourceProcessingStatus.Queued] = new() { SourceProcessingStatus.Processing },
        [SourceProcessingStatus.Processing] = new() { SourceProcessingStatus.Processed, SourceProcessingStatus.Failed },
        [SourceProcessingStatus.Processed] = new(),
        [SourceProcessingStatus.Failed] = new() { SourceProcessingStatus.Ready },
    };

    public SourceService(
        ISourceRepository sourceRepository,
        IWorldMemberRepository worldMemberRepository,
        ICampaignRepository campaignRepository,
        IExtractionQueueClient extractionQueueClient,
        IReviewBatchRepository reviewBatchRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IBlobStorageService blobStorage,
        ILogger<SourceService> logger)
    {
        _sourceRepository = sourceRepository;
        _worldMemberRepository = worldMemberRepository;
        _campaignRepository = campaignRepository;
        _extractionQueueClient = extractionQueueClient;
        _reviewBatchRepository = reviewBatchRepository;
        _sourceAttachmentRepository = sourceAttachmentRepository;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    public async Task<AppResult<Source>> CreateAsync(CreateSourceCommand command, CancellationToken ct)
    {
        // Role enforcement: Observer cannot create
        if (command.CreatingUserRole == WorldRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot create sources."));
        }

        // Input validation
        var titleError = ValidateTitle(command.Title);
        if (titleError is not null)
        {
            return AppResult<Source>.Fail(titleError);
        }

        var bodyError = ValidateBody(command.Body);
        if (bodyError is not null)
        {
            return AppResult<Source>.Fail(bodyError);
        }

        var uriError = ValidateUri(command.Uri);
        if (uriError is not null)
        {
            return AppResult<Source>.Fail(uriError);
        }

        // Player cannot set GMOnly visibility
        if (command.CreatingUserRole == WorldRole.Player && command.Visibility == VisibilityScope.GMOnly)
        {
            return AppResult<Source>.Fail(new AppError(400, "validation_error", "Players cannot create GMOnly sources."));
        }

        // Campaign, when declared, must belong to the same world
        if (command.CampaignId is not null)
        {
            var campaignError = await ValidateCampaignAsync(command.CampaignId.Value, command.WorldId, ct);
            if (campaignError is not null)
            {
                return AppResult<Source>.Fail(campaignError);
            }
        }

        var now = DateTimeOffset.UtcNow;

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            CampaignId = command.CampaignId,
            Type = command.Type,
            Title = command.Title,
            Body = command.Body,
            Uri = command.Uri,
            OccurredAt = command.OccurredAt,
            CreatedAt = now,
            CreatedByUserId = command.CreatingUserId,
            Visibility = command.Visibility,
            ProcessingStatus = SourceProcessingStatus.Draft
        };

        source = await _sourceRepository.CreateAsync(source, ct);

        return AppResult<Source>.Success(source);
    }

    public async Task<AppResult<Source>> GetByIdAsync(Guid sourceId, Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null || source.WorldId != worldId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Visibility enforcement — return not-found for invisible sources
        if (!CanSeeSource(source, requestingUserId, role))
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        return AppResult<Source>.Success(source);
    }

    public async Task<AppResult<Source>> UpdateAsync(UpdateSourceCommand command, CancellationToken ct)
    {
        // Role enforcement: Observer cannot update
        if (command.ActingUserRole == WorldRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot update sources."));
        }

        var source = await _sourceRepository.GetByIdAsync(command.SourceId, ct);

        if (source is null || source.WorldId != command.WorldId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Ownership enforcement: only creator or GM can update
        if (source.CreatedByUserId != command.ActingUserId && command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Source>.Fail(new AppError(403, "forbidden", "Only the source creator or a GM can update this source."));
        }

        // Processing status guards: in-flight sources are fully locked. Processed sources
        // allow metadata edits (title, campaign, date, uri, type) — but body changes must
        // go through reprocessing (the extracted knowledge derives from the body), and
        // visibility changes are blocked because derived artifacts are not rescoped.
        if (source.ProcessingStatus is SourceProcessingStatus.Queued or SourceProcessingStatus.Processing)
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                $"Source cannot be modified while in {source.ProcessingStatus} status."));
        }

        if (source.ProcessingStatus == SourceProcessingStatus.Processed)
        {
            // Value comparison, not presence: clients resend unchanged fields.
            if (command.Body is not null && command.Body != source.Body)
            {
                return AppResult<Source>.Fail(new AppError(409, "body_requires_reprocess",
                    "This source has been processed. Editing its body requires reprocessing, which deletes knowledge derived solely from it."));
            }

            if (command.Visibility is not null && command.Visibility != source.Visibility)
            {
                return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                    "Visibility cannot be changed after processing: knowledge derived from this source keeps its original scope."));
            }
        }

        // Validate optional Title if provided
        if (command.Title is not null)
        {
            var titleError = ValidateTitle(command.Title);
            if (titleError is not null)
            {
                return AppResult<Source>.Fail(titleError);
            }

            source.Title = command.Title;
        }

        // Validate optional Body if provided
        if (command.Body is not null)
        {
            var bodyError = ValidateBody(command.Body);
            if (bodyError is not null)
            {
                return AppResult<Source>.Fail(bodyError);
            }

            source.Body = command.Body;
        }

        // Validate optional Uri if provided
        if (command.Uri is not null)
        {
            var uriError = ValidateUri(command.Uri);
            if (uriError is not null)
            {
                return AppResult<Source>.Fail(uriError);
            }

            source.Uri = command.Uri;
        }

        if (command.OccurredAt is not null)
        {
            source.OccurredAt = command.OccurredAt;
        }

        if (command.Type is not null)
        {
            source.Type = command.Type.Value;
        }

        if (command.Visibility is not null)
        {
            // Player cannot set GMOnly visibility
            if (command.ActingUserRole == WorldRole.Player && command.Visibility == VisibilityScope.GMOnly)
            {
                return AppResult<Source>.Fail(new AppError(400, "validation_error", "Players cannot set GMOnly visibility."));
            }

            source.Visibility = command.Visibility.Value;
        }

        if (command.CampaignId is not null)
        {
            var campaignError = await ValidateCampaignAsync(command.CampaignId.Value, command.WorldId, ct);
            if (campaignError is not null)
            {
                return AppResult<Source>.Fail(campaignError);
            }

            source.CampaignId = command.CampaignId;
            // Drop the loaded navigation: EF relationship fixup would otherwise restore
            // the FK from the stale Campaign object when the entity is re-attached.
            source.Campaign = null;
        }
        else if (command.ClearCampaign)
        {
            source.CampaignId = null;
            source.Campaign = null;
        }

        source = await _sourceRepository.UpdateAsync(source, ct);

        return AppResult<Source>.Success(source);
    }

    public async Task<AppResult> DeleteAsync(Guid sourceId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        // Role enforcement: Observer cannot delete
        if (role == WorldRole.Observer)
        {
            return AppResult.Fail(new AppError(403, "insufficient_role", "Observers cannot delete sources."));
        }

        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);

        if (source is null || source.WorldId != worldId)
        {
            return AppResult.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Ownership enforcement: only creator or GM can delete
        if (source.CreatedByUserId != actingUserId && role != WorldRole.GM)
        {
            return AppResult.Fail(new AppError(403, "forbidden", "Only the source creator or a GM can delete this source."));
        }

        // Processing status guards: deletes blocked when Queued/Processing
        if (source.ProcessingStatus is SourceProcessingStatus.Queued or SourceProcessingStatus.Processing)
        {
            return AppResult.Fail(new AppError(409, "invalid_status",
                $"Source cannot be deleted while in {source.ProcessingStatus} status."));
        }

        // Attachment blobs first, failures swallowed (Library convention): an orphaned
        // blob beats an orphaned row pointing at nothing. Rows cascade with the source.
        foreach (var attachment in await _sourceAttachmentRepository.ListBySourceAsync(sourceId, ct))
        {
            try
            {
                await _blobStorage.DeleteBlobAsync(attachment.BlobPath, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Blob delete failed for attachment {AttachmentId}; deleting the source anyway", attachment.Id);
            }
        }

        // Review batches don't cascade from the source (SQL Server cascade-path limits) —
        // clear them explicitly. Pending proposals go with them; accepted knowledge stays.
        await _reviewBatchRepository.DeleteBySourceAsync(sourceId, ct);

        await _sourceRepository.DeleteAsync(sourceId, ct);

        return AppResult.Success();
    }

    public async Task<AppResult<IReadOnlyList<Source>>> ListByWorldAsync(Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct, Guid? campaignId = null, bool unassignedOnly = false)
    {
        var allSources = await _sourceRepository.ListByWorldAsync(worldId, cancellationToken: ct);

        var filtered = allSources.Where(s => CanSeeSource(s, requestingUserId, role));

        if (campaignId is not null)
        {
            filtered = filtered.Where(s => s.CampaignId == campaignId);
        }
        else if (unassignedOnly)
        {
            filtered = filtered.Where(s => s.CampaignId is null);
        }

        var visibleSources = filtered
            .OrderByDescending(s => s.CreatedAt)
            .ToList();

        return AppResult<IReadOnlyList<Source>>.Success(visibleSources);
    }

    public async Task<AppResult<Source>> MarkReadyAsync(MarkSourceReadyCommand command, CancellationToken ct)
    {
        // Role enforcement: Observer cannot mark ready
        if (command.ActingUserRole == WorldRole.Observer)
        {
            return AppResult<Source>.Fail(new AppError(403, "insufficient_role", "Observers cannot mark sources as ready."));
        }

        var source = await _sourceRepository.GetByIdAsync(command.SourceId, ct);

        if (source is null || source.WorldId != command.WorldId)
        {
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        // Ownership enforcement: only creator or GM can mark ready
        if (source.CreatedByUserId != command.ActingUserId && command.ActingUserRole != WorldRole.GM)
        {
            return AppResult<Source>.Fail(new AppError(403, "forbidden", "Only the source creator or a GM can mark this source as ready."));
        }

        // State machine: only Draft can transition to Ready
        if (!IsValidTransition(source.ProcessingStatus, SourceProcessingStatus.Ready))
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_transition",
                $"Cannot transition from {source.ProcessingStatus} to Ready."));
        }

        // Transition Draft → Ready
        source.ProcessingStatus = SourceProcessingStatus.Ready;
        source = await _sourceRepository.UpdateAsync(source, ct);

        // Commit Queued BEFORE enqueueing: the worker skips (and completes) any message
        // whose source is not Queued, and a warm worker can receive the message faster
        // than a post-enqueue status write lands — wedging the source at Queued forever.
        // Enqueue failure reverts to Ready so the user can retry.
        source.ProcessingStatus = SourceProcessingStatus.Queued;
        source = await _sourceRepository.UpdateAsync(source, ct);

        try
        {
            await _extractionQueueClient.SendExtractionMessageAsync(source.Id, source.WorldId, ct);
        }
        catch
        {
            source.ProcessingStatus = SourceProcessingStatus.Ready;
            source = await _sourceRepository.UpdateAsync(source, ct);
            return AppResult<Source>.Fail(new AppError(502, "enqueue_failed",
                "Failed to enqueue source for extraction. The source remains at Ready status."));
        }

        return AppResult<Source>.Success(source);
    }

    private async Task<AppError?> ValidateCampaignAsync(Guid campaignId, Guid worldId, CancellationToken ct)
    {
        var campaign = await _campaignRepository.GetByIdAsync(campaignId, ct);

        if (campaign is null || campaign.WorldId != worldId)
        {
            return new AppError(400, "invalid_campaign", "Campaign not found in this world.");
        }

        return null;
    }

    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };

    private static bool IsValidTransition(SourceProcessingStatus current, SourceProcessingStatus target)
    {
        return ValidTransitions.TryGetValue(current, out var validTargets) && validTargets.Contains(target);
    }

    // Shared with SourceReprocessService — one definition of the field rules.
    internal static AppError? ValidateTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new AppError(400, "validation_error", "Source title must not be empty or whitespace.");
        }

        if (title.Length > 200)
        {
            return new AppError(400, "validation_error", "Source title must be between 1 and 200 characters.");
        }

        return null;
    }

    internal static AppError? ValidateBody(string? body)
    {
        if (body is not null && body.Length > 100_000)
        {
            return new AppError(400, "validation_error", "Source body must not exceed 100,000 characters.");
        }

        return null;
    }

    internal static AppError? ValidateUri(string? uri)
    {
        if (uri is not null && uri.Length > 2_048)
        {
            return new AppError(400, "validation_error", "Source URI must not exceed 2,048 characters.");
        }

        return null;
    }
}
