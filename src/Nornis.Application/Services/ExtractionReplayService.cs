using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Re-extracts the world's timeline one source at a time, in story order, pausing for
/// human review between sources. Only the cursor source is ever in flight, so each note
/// is extracted against the knowledge its predecessors' ACCEPTED proposals established —
/// this is what makes carried-forward location and storyline context meaningful. The walk
/// advances from <see cref="TryAdvanceAsync"/>, called by the review pipeline when a
/// batch's last proposal resolves and by the extraction pipeline when a source yields
/// nothing to review.
/// </summary>
public class ExtractionReplayService : IExtractionReplayService
{
    private readonly IExtractionReplayRepository _replayRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceReprocessService _reprocessService;
    private readonly ILogger<ExtractionReplayService> _logger;

    /// <summary>Timeline source types a replay walks — the same set the Journey reads.</summary>
    private static readonly SourceType[] TimelineTypes =
        [SourceType.SessionNote, SourceType.Transcript, SourceType.SessionAudio, SourceType.ImportedNote];

    public ExtractionReplayService(
        IExtractionReplayRepository replayRepository,
        ISourceRepository sourceRepository,
        ISourceReprocessService reprocessService,
        ILogger<ExtractionReplayService> logger)
    {
        _replayRepository = replayRepository;
        _sourceRepository = sourceRepository;
        _reprocessService = reprocessService;
        _logger = logger;
    }

    public async Task<AppResult<int>> CountFromAsync(
        Guid worldId, Guid startSourceId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await AuthorizeStartAsync(worldId, startSourceId, actingUserRole, requireNoActiveReplay: false, ct);
        if (!gate.IsSuccess)
            return AppResult<int>.Fail(gate.Error!);

        var start = gate.Value!;
        var after = await _sourceRepository.CountExtractableTimelineAfterAsync(
            worldId, start.OccurredAt ?? start.CreatedAt, start.CreatedAt, ct);

        return AppResult<int>.Success(1 + after);
    }

    public async Task<AppResult<ExtractionReplayInfo>> StartAsync(
        Guid worldId, Guid startSourceId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await AuthorizeStartAsync(worldId, startSourceId, actingUserRole, requireNoActiveReplay: true, ct);
        if (!gate.IsSuccess)
            return AppResult<ExtractionReplayInfo>.Fail(gate.Error!);

        // The replay row must exist BEFORE the starting source is requeued: a fast worker
        // can complete a zero-proposal extraction and call TryAdvance almost immediately.
        var now = DateTimeOffset.UtcNow;
        var replay = await _replayRepository.CreateAsync(new ExtractionReplay
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CurrentSourceId = startSourceId,
            Status = ExtractionReplayStatus.Active,
            CreatedByUserId = actingUserId,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        var reprocess = await _reprocessService.ReprocessAsync(
            new ReprocessSourceCommand(startSourceId, worldId, actingUserId, actingUserRole), ct);
        if (!reprocess.IsSuccess)
        {
            // Nothing was requeued — retire the run rather than leaving a stalled Active row.
            replay.Status = ExtractionReplayStatus.Canceled;
            replay.UpdatedAt = DateTimeOffset.UtcNow;
            replay.CompletedAt = replay.UpdatedAt;
            await _replayRepository.UpdateAsync(replay, ct);
            return AppResult<ExtractionReplayInfo>.Fail(reprocess.Error!);
        }

        _logger.LogInformation(
            "Extraction replay started. ReplayId={ReplayId}, WorldId={WorldId}, StartSourceId={StartSourceId}",
            replay.Id, worldId, startSourceId);

        return AppResult<ExtractionReplayInfo>.Success(await BuildInfoAsync(replay, ct));
    }

    public async Task<AppResult<ExtractionReplayInfo?>> GetActiveAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        if (actingUserRole != WorldRole.GM)
            return AppResult<ExtractionReplayInfo?>.Fail(GmOnly());

        var replay = await _replayRepository.GetActiveByWorldAsync(worldId, ct);
        if (replay is null)
            return AppResult<ExtractionReplayInfo?>.Success(null);

        return AppResult<ExtractionReplayInfo?>.Success(await BuildInfoAsync(replay, ct));
    }

    public async Task<AppResult<ExtractionReplayInfo>> CancelAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        if (actingUserRole != WorldRole.GM)
            return AppResult<ExtractionReplayInfo>.Fail(GmOnly());

        var replay = await _replayRepository.GetActiveByWorldAsync(worldId, ct);
        if (replay is null)
            return AppResult<ExtractionReplayInfo>.Fail(
                new AppError(404, "not_found", "This world has no replay in progress."));

        replay.Status = ExtractionReplayStatus.Canceled;
        replay.UpdatedAt = DateTimeOffset.UtcNow;
        replay.CompletedAt = replay.UpdatedAt;
        await _replayRepository.UpdateAsync(replay, ct);

        _logger.LogInformation(
            "Extraction replay canceled. ReplayId={ReplayId}, WorldId={WorldId}, CursorSourceId={CursorSourceId}",
            replay.Id, worldId, replay.CurrentSourceId);

        return AppResult<ExtractionReplayInfo>.Success(await BuildInfoAsync(replay, ct));
    }

    public async Task TryAdvanceAsync(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        // Never let an advance failure surface into the review action or extraction that
        // triggered it — a stalled replay is recoverable (cancel and restart); a failed
        // review accept is data loss to the user.
        try
        {
            await AdvanceCoreAsync(worldId, sourceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Extraction replay advance failed. WorldId={WorldId}, SourceId={SourceId}",
                worldId, sourceId);
        }
    }

    private async Task AdvanceCoreAsync(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        var replay = await _replayRepository.GetActiveByWorldAsync(worldId, ct);
        if (replay is null || replay.CurrentSourceId != sourceId)
            return;

        var cursor = await _sourceRepository.GetByIdAsync(replay.CurrentSourceId, ct);
        if (cursor is null)
        {
            _logger.LogWarning(
                "Replay cursor source no longer exists; replay stalls until canceled. ReplayId={ReplayId}",
                replay.Id);
            return;
        }

        var pivotOccurred = cursor.OccurredAt ?? cursor.CreatedAt;
        var pivotCreated = cursor.CreatedAt;

        // Walk forward until a source reprocesses cleanly. A source whose status shifted
        // between the query and the cascade (rare) is skipped rather than wedging the run;
        // the bound is a backstop against pathological churn, not an expected exit.
        for (var hops = 0; hops < 100; hops++)
        {
            var next = (await _sourceRepository.ListExtractableTimelineAfterAsync(
                worldId, pivotOccurred, pivotCreated, maxCount: 1, ct)).FirstOrDefault();

            if (next is null)
            {
                replay.Status = ExtractionReplayStatus.Completed;
                replay.UpdatedAt = DateTimeOffset.UtcNow;
            replay.CompletedAt = replay.UpdatedAt;
                await _replayRepository.UpdateAsync(replay, ct);
                _logger.LogInformation(
                    "Extraction replay completed. ReplayId={ReplayId}, WorldId={WorldId}",
                    replay.Id, worldId);
                return;
            }

            // Claim the cursor BEFORE reprocessing: if two batch completions race, the
            // second claim hits the concurrency token and aborts instead of double-queueing.
            replay.CurrentSourceId = next.Id;
            replay.UpdatedAt = DateTimeOffset.UtcNow;
            await _replayRepository.UpdateAsync(replay, ct);

            var reprocess = await _reprocessService.ReprocessAsync(
                new ReprocessSourceCommand(next.Id, worldId, replay.CreatedByUserId, WorldRole.GM), ct);
            if (reprocess.IsSuccess)
            {
                _logger.LogInformation(
                    "Extraction replay advanced. ReplayId={ReplayId}, WorldId={WorldId}, NextSourceId={NextSourceId}",
                    replay.Id, worldId, next.Id);
                return;
            }

            _logger.LogWarning(
                "Replay skipped a source that would not reprocess ({Code}); continuing. " +
                "ReplayId={ReplayId}, SourceId={SourceId}",
                reprocess.Error?.Code, replay.Id, next.Id);
            pivotOccurred = next.OccurredAt ?? next.CreatedAt;
            pivotCreated = next.CreatedAt;
        }

        _logger.LogError(
            "Replay advance exceeded its hop bound without finding a processable source. ReplayId={ReplayId}",
            replay.Id);
    }

    /// <summary>Shared start gate: GM only, source exists in this world, is a timeline
    /// type with extraction enabled, and is in a reprocessable state.</summary>
    private async Task<AppResult<Source>> AuthorizeStartAsync(
        Guid worldId, Guid startSourceId, WorldRole actingUserRole, bool requireNoActiveReplay, CancellationToken ct)
    {
        if (actingUserRole != WorldRole.GM)
            return AppResult<Source>.Fail(GmOnly());

        if (requireNoActiveReplay && await _replayRepository.GetActiveByWorldAsync(worldId, ct) is not null)
            return AppResult<Source>.Fail(new AppError(409, "replay_active",
                "A replay is already in progress for this world. Cancel it before starting another."));

        var source = await _sourceRepository.GetByIdAsync(startSourceId, ct);
        if (source is null || source.WorldId != worldId)
            return AppResult<Source>.Fail(new AppError(404, "not_found", "Source not found."));

        if (!TimelineTypes.Contains(source.Type))
            return AppResult<Source>.Fail(new AppError(400, "not_replayable",
                "A replay starts from a session or imported note on the timeline."));

        if (!source.ExtractionEnabled)
            return AppResult<Source>.Fail(new AppError(400, "not_replayable",
                "This source is stored without extraction and cannot anchor a replay."));

        if (source.ProcessingStatus is not (SourceProcessingStatus.Processed or SourceProcessingStatus.Failed))
            return AppResult<Source>.Fail(new AppError(409, "invalid_status",
                $"Only Processed or Failed sources can start a replay; this source is {source.ProcessingStatus}."));

        return AppResult<Source>.Success(source);
    }

    private async Task<ExtractionReplayInfo> BuildInfoAsync(ExtractionReplay replay, CancellationToken ct)
    {
        var cursor = await _sourceRepository.GetByIdAsync(replay.CurrentSourceId, ct);
        var remaining = cursor is null
            ? 0
            : await _sourceRepository.CountExtractableTimelineAfterAsync(
                replay.WorldId, cursor.OccurredAt ?? cursor.CreatedAt, cursor.CreatedAt, ct);

        return new ExtractionReplayInfo(
            replay.Id,
            replay.Status.ToString(),
            replay.CurrentSourceId,
            cursor?.Title,
            cursor?.ProcessingStatus.ToString(),
            remaining,
            replay.CreatedAt);
    }

    private static AppError GmOnly() =>
        new(403, "insufficient_role", "Only the GM can manage an extraction replay.");
}
