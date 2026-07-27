using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Walks a GM's campaign backlog one note at a time. The whole point is the pacing: each
/// note is marked ready only once its predecessor's proposals have been vetted, so every
/// extraction runs against canon a human has already approved instead of a dozen sources
/// racing against the same stale snapshot and each proposing their own copy of everything.
///
/// Held notes are ordinary Sources parked at <see cref="SourceProcessingStatus.Draft"/>;
/// advancing routes through <see cref="ISourceService.MarkReadyAsync"/> so the
/// commit-Queued-before-enqueue invariant is never re-implemented here. Item state is
/// derived from the source on every read — the only thing this flow stores about an item
/// is whether the GM skipped it.
/// </summary>
public class ImportSessionService : IImportSessionService
{
    private readonly IImportSessionRepository _sessionRepository;
    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewProposalRepository _proposalRepository;
    private readonly ISourceService _sourceService;
    private readonly ILogger<ImportSessionService> _logger;

    /// <summary>Backlog notes are imported notes: a timeline source type, so the replay,
    /// journey, and "before this moment" context queries all see them.</summary>
    private const SourceType ImportedNoteType = SourceType.ImportedNote;

    public ImportSessionService(
        IImportSessionRepository sessionRepository,
        ISourceRepository sourceRepository,
        IReviewProposalRepository proposalRepository,
        ISourceService sourceService,
        ILogger<ImportSessionService> logger)
    {
        _sessionRepository = sessionRepository;
        _sourceRepository = sourceRepository;
        _proposalRepository = proposalRepository;
        _sourceService = sourceService;
        _logger = logger;
    }

    public async Task<AppResult<ImportSessionInfo>> CreateAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        if (actingUserRole != WorldRole.GM)
        {
            return AppResult<ImportSessionInfo>.Fail(GmOnly());
        }

        if (await _sessionRepository.GetNonTerminalByWorldAsync(worldId, ct) is not null)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(409, "import_session_active",
                "This world already has an import in progress. Finish or abandon it before starting another."));
        }

        var now = DateTimeOffset.UtcNow;
        var session = await _sessionRepository.CreateAsync(new ImportSession
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CreatedByUserId = actingUserId,
            Status = ImportSessionStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now
        }, ct);

        _logger.LogInformation(
            "Import session created. SessionId={SessionId}, WorldId={WorldId}", session.Id, worldId);

        return AppResult<ImportSessionInfo>.Success(await BuildInfoAsync(session, ct));
    }

    public async Task<AppResult<ImportSessionInfo>> GetCurrentAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        if (actingUserRole != WorldRole.GM)
        {
            return AppResult<ImportSessionInfo>.Fail(GmOnly());
        }

        var session = await _sessionRepository.GetNonTerminalByWorldAsync(worldId, ct);
        if (session is null)
        {
            return AppResult<ImportSessionInfo>.Fail(
                new AppError(404, "not_found", "This world has no import in progress."));
        }

        return AppResult<ImportSessionInfo>.Success(await BuildInfoAsync(session, ct));
    }

    public async Task<AppResult<ImportSessionInfo>> AddItemAsync(AddImportNoteCommand command, CancellationToken ct)
    {
        var gate = await LoadOpenAsync(command.WorldId, command.SessionId, command.ActingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(gate.Error!);
        }

        var session = gate.Value!;

        // Field validation, visibility rules, and the Draft starting status all come from
        // the normal capture path — an imported note is an ordinary source in every way.
        var created = await _sourceService.CreateAsync(new CreateSourceCommand(
            WorldId: command.WorldId,
            Title: command.Title,
            Type: ImportedNoteType,
            Visibility: VisibilityScope.PartyVisible,
            CreatingUserId: command.ActingUserId,
            CreatingUserRole: command.ActingUserRole,
            Body: command.Body,
            OccurredAt: command.OccurredAt), ct);

        if (!created.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(created.Error!);
        }

        var nextPosition = session.Items.Count == 0 ? 0 : session.Items.Max(i => i.Position) + 1;

        await _sessionRepository.AddItemAsync(new ImportSessionItem
        {
            Id = Guid.NewGuid(),
            ImportSessionId = session.Id,
            SourceId = created.Value!.Id,
            Position = nextPosition,
            Skipped = false
        }, ct);

        await TouchAsync(session, ct);

        return await ReloadAsync(session.Id, ct);
    }

    public async Task<AppResult<ImportSessionInfo>> ReorderAsync(
        Guid worldId, Guid sessionId, IReadOnlyList<Guid> orderedItemIds,
        Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await LoadOpenAsync(worldId, sessionId, actingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(gate.Error!);
        }

        var session = gate.Value!;
        var view = await BuildInfoAsync(session, ct);

        // Only notes that have not started can move; a note already extracted (or extracting)
        // has knowledge hanging off its place in the walk.
        var movable = view.Items.Where(i => i.State == ImportItemState.Waiting).ToList();

        var submitted = orderedItemIds.Distinct().ToList();
        if (submitted.Count != orderedItemIds.Count
            || submitted.Count != movable.Count
            || !submitted.All(id => movable.Any(m => m.Id == id)))
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(400, "invalid_order",
                "The order must list every not-yet-started item exactly once."));
        }

        // The movable items are rearranged among the position slots they already occupy, so
        // items that have started keep their exact place in the walk.
        var slots = movable.Select(m => m.Position).OrderBy(p => p).ToList();
        var positions = orderedItemIds.Select((id, index) => (ItemId: id, Position: slots[index])).ToList();

        await _sessionRepository.SetItemPositionsAsync(positions, ct);
        await TouchAsync(session, ct);

        return await ReloadAsync(session.Id, ct);
    }

    public async Task<AppResult<ImportSessionInfo>> DeleteItemAsync(
        Guid worldId, Guid sessionId, Guid itemId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await LoadOpenAsync(worldId, sessionId, actingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(gate.Error!);
        }

        var session = gate.Value!;
        var item = session.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(404, "not_found", "Import item not found."));
        }

        var source = await _sourceRepository.GetByIdAsync(item.SourceId, ct);
        if (source is not null && source.ProcessingStatus != SourceProcessingStatus.Draft)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(409, "item_started",
                "This note has already been sent for extraction and can no longer be removed from the import."));
        }

        // The source first: if it refuses to delete, the item stays and the walk is unchanged.
        // The note was created by this flow and has produced no knowledge yet, so it goes with it.
        if (source is not null)
        {
            var deleted = await _sourceService.DeleteAsync(item.SourceId, worldId, actingUserId, actingUserRole, ct);
            if (!deleted.IsSuccess)
            {
                return AppResult<ImportSessionInfo>.Fail(deleted.Error!);
            }
        }

        await _sessionRepository.DeleteItemAsync(itemId, ct);
        await TouchAsync(session, ct);

        return await ReloadAsync(session.Id, ct);
    }

    public async Task<AppResult<ImportSessionInfo>> StartAsync(
        Guid worldId, Guid sessionId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await LoadOpenAsync(worldId, sessionId, actingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(gate.Error!);
        }

        var session = gate.Value!;
        if (session.Status != ImportSessionStatus.Draft)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(409, "invalid_status",
                "This import has already started."));
        }

        if (session.Items.Count == 0)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(400, "no_items",
                "Add at least one note before starting the import."));
        }

        await _sessionRepository.UpdateAsync(session.Id, ImportSessionStatus.InProgress, DateTimeOffset.UtcNow, ct);

        var view = await BuildInfoAsync(session, ct);
        var started = await StartNextAsync(session, view, worldId, actingUserId, actingUserRole, ct);
        if (!started.IsSuccess)
        {
            // Nothing was queued — put the session back in Draft rather than leave a run that
            // looks live but has no note in flight.
            await _sessionRepository.UpdateAsync(session.Id, ImportSessionStatus.Draft, DateTimeOffset.UtcNow, ct);
            return AppResult<ImportSessionInfo>.Fail(started.Error!);
        }

        _logger.LogInformation(
            "Import session started. SessionId={SessionId}, WorldId={WorldId}, Items={ItemCount}",
            session.Id, worldId, session.Items.Count);

        return await ReloadAsync(session.Id, ct);
    }

    public async Task<AppResult<ImportSessionInfo>> AdvanceAsync(
        Guid worldId, Guid sessionId, bool skipCurrent, Guid? expectedItemId,
        Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await LoadOpenAsync(worldId, sessionId, actingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(gate.Error!);
        }

        var session = gate.Value!;
        if (session.Status != ImportSessionStatus.InProgress)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(409, "invalid_status",
                "Start the import before advancing it."));
        }

        var view = await BuildInfoAsync(session, ct);
        var current = view.Items.FirstOrDefault(i => i.Id == view.CurrentItemId);

        // Nothing left that is neither done nor skipped: the walk is over.
        if (current is null)
        {
            await CompleteAsync(session, ct);
            return await ReloadAsync(session.Id, ct);
        }

        // The walk may have moved on since the screen was drawn (another tab, a retried click).
        // Acting on a note the user is not looking at would silently skip it.
        if (expectedItemId is { } expected && expected != current.Id)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(409, "item_moved",
                "The import has moved on to a different note. Refresh to see where it stands."));
        }

        if (skipCurrent)
        {
            await _sessionRepository.SetItemSkippedAsync(current.Id, true, ct);
            _logger.LogInformation(
                "Import item skipped. SessionId={SessionId}, ItemId={ItemId}, SourceId={SourceId}, State={State}",
                session.Id, current.Id, current.SourceId, current.State);
        }
        else if (current.State != ImportItemState.Waiting)
        {
            // The current note is still in flight. "Waiting" is the only state that means the
            // note before it landed and this one has not started — everything else is the
            // user being asked to finish what is on screen first.
            return AppResult<ImportSessionInfo>.Fail(new AppError(409, "item_not_ready",
                current.State switch
                {
                    ImportItemState.Reviewing =>
                        $"“{current.Title}” still has {current.OpenProposalCount} proposal(s) waiting on a decision.",
                    ImportItemState.Failed =>
                        $"Extraction failed for “{current.Title}”. Retry it or skip it.",
                    _ => $"“{current.Title}” is still being extracted."
                }));
        }

        // A skip changed which item is current; without one the walk is where it was.
        var nextView = view;
        if (skipCurrent)
        {
            var refreshed = await _sessionRepository.GetByIdAsync(session.Id, ct);
            if (refreshed is null)
            {
                return AppResult<ImportSessionInfo>.Fail(new AppError(404, "not_found", "Import session not found."));
            }

            nextView = await BuildInfoAsync(refreshed, ct);
        }

        var started = await StartNextAsync(session, nextView, worldId, actingUserId, actingUserRole, ct);
        if (!started.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(started.Error!);
        }

        return await ReloadAsync(session.Id, ct);
    }

    public async Task<AppResult<ImportSessionInfo>> AbandonAsync(
        Guid worldId, Guid sessionId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gate = await LoadOpenAsync(worldId, sessionId, actingUserRole, ct);
        if (!gate.IsSuccess)
        {
            return AppResult<ImportSessionInfo>.Fail(gate.Error!);
        }

        var session = gate.Value!;

        // Nothing is deleted: notes already processed keep their knowledge, and notes still
        // held stay as ordinary draft sources the GM can handle however they like.
        await _sessionRepository.UpdateAsync(session.Id, ImportSessionStatus.Abandoned, DateTimeOffset.UtcNow, ct);

        _logger.LogInformation(
            "Import session abandoned. SessionId={SessionId}, WorldId={WorldId}", session.Id, worldId);

        session.Status = ImportSessionStatus.Abandoned;
        return AppResult<ImportSessionInfo>.Success(await BuildInfoAsync(session, ct));
    }

    // ------------------------------------------------------------------ internals --

    /// <summary>
    /// Marks the walk's current note ready, or completes the session when nothing is left.
    /// Shared by start and advance — both mean "put the next note in the worker's hands".
    /// </summary>
    private async Task<AppResult> StartNextAsync(
        ImportSession session, ImportSessionInfo view, Guid worldId,
        Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var next = view.Items.FirstOrDefault(i => i.Id == view.CurrentItemId);
        if (next is null)
        {
            await CompleteAsync(session, ct);
            return AppResult.Success();
        }

        // Only a note that is not already in the worker's hands gets queued: never yet sent
        // (Draft), stranded by a failed enqueue (Ready), or failed and retryable (Failed).
        // Queued and Processing are left alone rather than double-queued.
        if (next.ProcessingStatus is not (SourceProcessingStatus.Draft
            or SourceProcessingStatus.Ready
            or SourceProcessingStatus.Failed))
        {
            return AppResult.Success();
        }

        var ready = await _sourceService.MarkReadyAsync(
            new MarkSourceReadyCommand(next.SourceId, worldId, actingUserId, actingUserRole), ct);

        if (!ready.IsSuccess)
        {
            return AppResult.Fail(ready.Error!);
        }

        _logger.LogInformation(
            "Import advanced to the next note. SessionId={SessionId}, ItemId={ItemId}, SourceId={SourceId}",
            session.Id, next.Id, next.SourceId);

        return AppResult.Success();
    }

    private async Task CompleteAsync(ImportSession session, CancellationToken ct)
    {
        await _sessionRepository.UpdateAsync(session.Id, ImportSessionStatus.Completed, DateTimeOffset.UtcNow, ct);

        _logger.LogInformation(
            "Import session completed. SessionId={SessionId}, WorldId={WorldId}", session.Id, session.WorldId);
    }

    private Task TouchAsync(ImportSession session, CancellationToken ct) =>
        _sessionRepository.TouchAsync(session.Id, DateTimeOffset.UtcNow, ct);

    private async Task<AppResult<ImportSessionInfo>> ReloadAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId, ct);
        if (session is null)
        {
            return AppResult<ImportSessionInfo>.Fail(new AppError(404, "not_found", "Import session not found."));
        }

        return AppResult<ImportSessionInfo>.Success(await BuildInfoAsync(session, ct));
    }

    /// <summary>GM gate plus "this session exists, in this world, and is still open".</summary>
    private async Task<AppResult<ImportSession>> LoadOpenAsync(
        Guid worldId, Guid sessionId, WorldRole actingUserRole, CancellationToken ct)
    {
        if (actingUserRole != WorldRole.GM)
        {
            return AppResult<ImportSession>.Fail(GmOnly());
        }

        var session = await _sessionRepository.GetByIdAsync(sessionId, ct);
        if (session is null || session.WorldId != worldId)
        {
            return AppResult<ImportSession>.Fail(new AppError(404, "not_found", "Import session not found."));
        }

        if (session.Status is not (ImportSessionStatus.Draft or ImportSessionStatus.InProgress))
        {
            return AppResult<ImportSession>.Fail(new AppError(409, "invalid_status",
                $"This import is {session.Status} and can no longer be changed."));
        }

        return AppResult<ImportSession>.Success(session);
    }

    private async Task<ImportSessionInfo> BuildInfoAsync(ImportSession session, CancellationToken ct)
    {
        var items = session.Items.OrderBy(i => i.Position).ToList();

        // One read of the world's sources rather than one per item; the same shape
        // SourceService.ListByWorldAsync uses.
        var sources = (await _sourceRepository.ListByWorldAsync(session.WorldId, cancellationToken: ct))
            .ToDictionary(s => s.Id);

        var openCounts = await _proposalRepository.CountOpenBySourcesAsync(
            items.Select(i => i.SourceId).ToList(), ct);

        var infos = new List<ImportItemInfo>(items.Count);
        foreach (var item in items)
        {
            sources.TryGetValue(item.SourceId, out var source);
            var openCount = openCounts.TryGetValue(item.SourceId, out var count) ? count : 0;

            infos.Add(new ImportItemInfo(
                item.Id,
                item.SourceId,
                item.Position,
                item.Skipped,
                // A source deleted outside this flow leaves a stub the walk steps past rather
                // than wedging on.
                source?.Title ?? "(removed)",
                source?.OccurredAt,
                source?.ProcessingStatus ?? SourceProcessingStatus.Processed,
                DeriveState(item, source, openCount),
                openCount));
        }

        var current = infos.FirstOrDefault(
            i => i.State is not (ImportItemState.Done or ImportItemState.Skipped));

        return new ImportSessionInfo(
            session.Id,
            session.WorldId,
            session.Status,
            session.CreatedAt,
            session.UpdatedAt,
            infos,
            current?.Id,
            current is null ? null : infos.IndexOf(current) + 1,
            infos.Count(i => i.State is ImportItemState.Done or ImportItemState.Skipped));
    }

    /// <summary>
    /// The item's state, read entirely off its source. Skipped wins over everything; a source
    /// that vanished counts as done so a deleted note cannot stall the walk.
    /// </summary>
    private static ImportItemState DeriveState(ImportSessionItem item, Source? source, int openProposalCount)
    {
        if (item.Skipped)
        {
            return ImportItemState.Skipped;
        }

        if (source is null)
        {
            return ImportItemState.Done;
        }

        return source.ProcessingStatus switch
        {
            // Ready counts as waiting, not extracting. Mark-ready commits Queued before it
            // enqueues, so a source sitting at Ready is one whose enqueue failed — it is NOT
            // in the worker's hands. Calling it waiting is what lets the GM re-trigger it
            // (Ready → Ready is a legal transition) instead of watching a spinner forever.
            SourceProcessingStatus.Draft or SourceProcessingStatus.Ready => ImportItemState.Waiting,
            SourceProcessingStatus.Queued or SourceProcessingStatus.Processing
                => ImportItemState.Extracting,
            SourceProcessingStatus.Failed => ImportItemState.Failed,
            SourceProcessingStatus.Processed => openProposalCount > 0
                ? ImportItemState.Reviewing
                : ImportItemState.Done,
            _ => ImportItemState.Waiting
        };
    }

    private static AppError GmOnly() =>
        new(403, "insufficient_role", "Only the GM can run a campaign import.");
}
