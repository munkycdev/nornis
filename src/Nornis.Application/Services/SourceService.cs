using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Messaging;
using Nornis.Application.Models;
using Nornis.Application.Storage;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class SourceService : ISourceService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly ICampaignRepository _campaignRepository;
    private readonly IExtractionQueueClient _extractionQueueClient;
    private readonly IReviewBatchRepository _reviewBatchRepository;
    private readonly IReviewProposalRepository _reviewProposalRepository;
    private readonly ISourceAttachmentRepository _sourceAttachmentRepository;
    private readonly IBlobStorageService _blobStorage;
    private readonly ILogger<SourceService> _logger;

    private static readonly Dictionary<SourceProcessingStatus, HashSet<SourceProcessingStatus>> ValidTransitions = new()
    {
        [SourceProcessingStatus.Draft] = new() { SourceProcessingStatus.Ready },
        // Ready → Ready lets mark-ready retry a source stranded at Ready (enqueue failure).
        [SourceProcessingStatus.Ready] = new() { SourceProcessingStatus.Queued, SourceProcessingStatus.Ready },
        // Queued → Ready is the way out of the dead-letter wedge, and it is gated on
        // staleness rather than allowed outright — see StaleQueuedThreshold below.
        [SourceProcessingStatus.Queued] = new() { SourceProcessingStatus.Processing, SourceProcessingStatus.Ready },
        [SourceProcessingStatus.Processing] = new() { SourceProcessingStatus.Processed, SourceProcessingStatus.Failed },
        [SourceProcessingStatus.Processed] = new(),
        [SourceProcessingStatus.Failed] = new() { SourceProcessingStatus.Ready },
    };

    /// <summary>
    /// How long a source must sit at Queued before a GM may re-ready it.
    ///
    /// Sized against the worst case the queue can produce: five deliveries, each able to hold
    /// the lock for <c>MaxAutoLockRenewalDuration</c> (5 minutes for extraction) plus up to two
    /// minutes of <c>RedeliveryBackoff</c> — about thirty-five minutes before the message
    /// dead-letters. An hour clears that with room for a cold worker scaling from zero and
    /// pulling an image.
    ///
    /// Past this point the message is on the dead-letter queue or gone, so re-enqueueing cannot
    /// race a live delivery. And if a pathological run *is* somehow still going,
    /// IX_ReviewBatches_SourceId_Extraction means only one of them commits a batch — which is
    /// what makes this safe now and did not when this fix was first assessed and declined.
    /// The cost of being wrong is one wasted extraction, not a duplicated record.
    /// </summary>
    public static readonly TimeSpan StaleQueuedThreshold = TimeSpan.FromHours(1);

    public SourceService(
        ISourceRepository sourceRepository,
        IWorldMemberRepository worldMemberRepository,
        ICampaignRepository campaignRepository,
        IExtractionQueueClient extractionQueueClient,
        IReviewBatchRepository reviewBatchRepository,
        IReviewProposalRepository reviewProposalRepository,
        ISourceAttachmentRepository sourceAttachmentRepository,
        IBlobStorageService blobStorage,
        ILogger<SourceService> logger)
    {
        _sourceRepository = sourceRepository;
        _worldMemberRepository = worldMemberRepository;
        _campaignRepository = campaignRepository;
        _extractionQueueClient = extractionQueueClient;
        _reviewBatchRepository = reviewBatchRepository;
        _reviewProposalRepository = reviewProposalRepository;
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
            ProcessingStatus = SourceProcessingStatus.Draft,
            ExtractionEnabled = command.ExtractionEnabled
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
            // Value comparison, not presence: clients resend unchanged fields. A clear is
            // a change too — emptying an extracted source's body invalidates exactly the
            // derived knowledge the reprocess gate exists to protect.
            var bodyChanged = (command.Body is not null && command.Body != source.Body)
                || (command.ClearBody && source.Body is not null);
            var visibilityChanged = command.Visibility is not null && command.Visibility != source.Visibility;

            if (bodyChanged || visibilityChanged)
            {
                // Only extraction locks these fields: a source stored without extraction
                // has no derived knowledge to invalidate and stays freely editable.
                var extracted = await _reviewBatchRepository.GetBySourceIdAsync(source.Id, ct) is not null;

                if (extracted && bodyChanged)
                {
                    return AppResult<Source>.Fail(new AppError(409, "body_requires_reprocess",
                        "This source has been processed. Editing its body requires reprocessing, which deletes knowledge derived solely from it."));
                }

                if (extracted && visibilityChanged)
                {
                    return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                        "Visibility cannot be changed after processing: knowledge derived from this source keeps its original scope."));
                }
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
        else if (command.ClearBody)
        {
            // Null means "unchanged" in a partial update, so emptying the editor has to be
            // said explicitly — the same idiom as ClearOccurredAt.
            source.Body = null;
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
        else if (command.ClearUri)
        {
            source.Uri = null;
        }

        if (command.OccurredAt is not null)
        {
            source.OccurredAt = command.OccurredAt;
        }
        else if (command.ClearOccurredAt)
        {
            source.OccurredAt = null;
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

        if (command.ExtractionEnabled is { } extractionEnabled && extractionEnabled != source.ExtractionEnabled)
        {
            source.ExtractionEnabled = extractionEnabled;

            // Re-enabling extraction on a stored (never-extracted) Processed source drops
            // it back to Ready so the Process action becomes available again.
            if (extractionEnabled
                && source.ProcessingStatus == SourceProcessingStatus.Processed
                && await _reviewBatchRepository.GetBySourceIdAsync(source.Id, ct) is null)
            {
                source.ProcessingStatus = SourceProcessingStatus.Ready;
            }
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

    // ListByWorldAsync (load every full row, filter in memory) was removed once both of its
    // callers moved to ListSummariesByWorldAsync. It is deliberately not kept "just in case":
    // it pulls Body and DerivedText for every source in the world, and leaving it available is
    // how that cost comes back the next time someone needs a list.

    public async Task<AppResult<IReadOnlyList<SourceListItem>>> ListSummariesByWorldAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct,
        Guid? campaignId = null, bool unassignedOnly = false)
    {
        var items = await _sourceRepository.ListSummariesByWorldAsync(
            worldId, requestingUserId, role, campaignId, unassignedOnly, ct);

        return AppResult<IReadOnlyList<SourceListItem>>.Success(items);
    }

    /// <summary>
    /// Two aggregate queries. The badge this feeds is polled every few seconds from every open
    /// tab, and used to be answered by loading every source in the world (including the
    /// unbounded Body and DerivedText columns) twice, plus the review queue's proposals,
    /// batches and artifacts — then discarding all of it to return six integers.
    /// </summary>
    public async Task<AppResult<SourceActivity>> GetActivityAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var byStatus = await _sourceRepository.CountByStatusAsync(worldId, requestingUserId, role, ct);

        // Same cap as the review queue, so the badge and the queue agree on when it is reached.
        var (pending, capped) = await _reviewProposalRepository.CountOpenForReviewerAsync(
            worldId, requestingUserId, role, ReviewService.ReviewQueueLimit, ct);

        return AppResult<SourceActivity>.Success(new SourceActivity(
            Ready: byStatus.GetValueOrDefault(SourceProcessingStatus.Ready),
            Queued: byStatus.GetValueOrDefault(SourceProcessingStatus.Queued),
            Processing: byStatus.GetValueOrDefault(SourceProcessingStatus.Processing),
            Failed: byStatus.GetValueOrDefault(SourceProcessingStatus.Failed),
            PendingProposals: pending,
            PendingProposalsCapped: capped));
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

        // State machine: only Draft, Ready (retry), Failed, and a long-stuck Queued.
        if (!IsValidTransition(source.ProcessingStatus, SourceProcessingStatus.Ready))
        {
            return AppResult<Source>.Fail(new AppError(409, "invalid_transition",
                $"Cannot transition from {source.ProcessingStatus} to Ready."));
        }

        // The wedge: a dead-lettered extraction leaves a source Queued with nothing coming for
        // it, and until now no user-reachable path out. Re-readying it early would be the worse
        // bug — a second extraction alongside a live one, paying twice — so it waits until the
        // queue can no longer be holding the message.
        if (source.ProcessingStatus == SourceProcessingStatus.Queued
            && !IsStaleQueued(source, DateTimeOffset.UtcNow))
        {
            return AppResult<Source>.Fail(new AppError(409, "still_queued",
                "This source is queued for extraction. If it is still queued in an hour, "
                + "marking it ready again will retry it."));
        }

        // Stored without extraction: "processing" just files the source in the record —
        // straight to Processed, no queue, no batch, no proposals.
        if (!source.ExtractionEnabled)
        {
            source.ProcessingStatus = SourceProcessingStatus.Processed;
            source = await _sourceRepository.UpdateAsync(source, ct);
            return AppResult<Source>.Success(source);
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

    /// <summary>
    /// Who may read this source's title and body. Two gates, and both must pass — the
    /// draft gate can only ever narrow what visibility already allows, never widen it.
    ///
    /// A Draft source has not been submitted: nobody has vetted it, and it has produced no
    /// canon. Its Visibility governs the knowledge it will yield once extracted, not who may
    /// read it while it waits — so until it is marked ready it belongs to its author and the
    /// GM alone. Capture's draft window is seconds, but the campaign backlog import parks a
    /// whole backlog at Draft for as long as the GM takes to walk it, and this list is also
    /// served to the anonymous public world page.
    ///
    /// The public page reads as Observer with <see cref="Guid.Empty"/>, so an ownership test
    /// must never treat an empty id as a match: unattributable rows fail closed, exactly as
    /// they do in <see cref="VisibilityFilter"/>.
    /// </summary>
    /// <remarks>
    /// The rule itself lives in <see cref="SourceVisibilityRule"/> so that the SQL used for
    /// counts and the in-memory filter used here are the same definition. Keeping a second copy
    /// here is how a badge count starts disagreeing with the list it summarises.
    ///
    /// The compiled delegate is memoised because compiling one builds and JIT-compiles a fresh
    /// <c>DynamicMethod</c>. This service is scoped, and every call within a request carries the
    /// same caller, so in practice the cache holds exactly one entry and every single-source read
    /// after the first is free.
    /// </remarks>
    private (Guid UserId, WorldRole Role, Func<Source, bool> Predicate)? _canSeeCache;

    private Func<Source, bool> CanSeePredicate(Guid userId, WorldRole role)
    {
        if (_canSeeCache is { } cached && cached.UserId == userId && cached.Role == role)
        {
            return cached.Predicate;
        }

        var predicate = SourceVisibilityRule.Compile(userId, role);
        _canSeeCache = (userId, role, predicate);
        return predicate;
    }

    private bool CanSeeSource(Source source, Guid userId, WorldRole role) =>
        CanSeePredicate(userId, role)(source);

    /// <summary>
    /// A source is safely re-readyable once it has been Queued longer than
    /// <see cref="StaleQueuedThreshold"/>. A null stamp means the row predates the column and
    /// has not moved since — which is itself older than any threshold, so it qualifies.
    /// </summary>
    private static bool IsStaleQueued(Source source, DateTimeOffset now) =>
        source.StatusChangedAt is not { } changedAt || now - changedAt >= StaleQueuedThreshold;

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
