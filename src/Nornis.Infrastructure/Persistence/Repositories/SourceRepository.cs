using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class SourceRepository : ISourceRepository
{
    private readonly NornisDbContext _context;

    public SourceRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default)
    {
        _context.Sources.Add(source);
        await _context.SaveChangesAsync(cancellationToken);
        await LoadCampaignAsync(source, cancellationToken);
        return source;
    }

    public async Task<int> CountAwaitingExtractionAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Sources
            .AsNoTracking()
            .CountAsync(
                s => s.ProcessingStatus == SourceProcessingStatus.Queued
                    || s.ProcessingStatus == SourceProcessingStatus.Processing,
                cancellationToken);
    }

    public async Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Sources
            .AsNoTracking()
            .Include(s => s.Campaign)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<SourceListItem>> ListSummariesByWorldAsync(
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        Guid? campaignId = null,
        bool unassignedOnly = false,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId)
            // Same shared rule the counts and the in-memory filter use.
            .Where(SourceVisibilityRule.CanSee(requestingUserId, role));

        if (campaignId is not null)
        {
            query = query.Where(s => s.CampaignId == campaignId);
        }
        else if (unassignedOnly)
        {
            query = query.Where(s => s.CampaignId == null);
        }

        // Projected, not Include'd: the campaign name is pulled through the navigation without
        // materialising the campaign, and Body/DerivedText are never touched.
        // Id breaks ties: the demo template stamps every source with the same CreatedAt, and SQL
        // leaves order within a tied group unspecified — so without this the sources list could
        // reshuffle between the four-second polls.
        return await query
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new SourceListItem(
                s.Id,
                s.WorldId,
                s.Type,
                s.Title,
                s.OccurredAt,
                s.CreatedAt,
                s.CreatedByUserId,
                s.Visibility,
                s.ProcessingStatus,
                s.CampaignId,
                s.Campaign != null ? s.Campaign.Name : null))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyCreatedAfterAsync(
        Guid worldId,
        DateTimeOffset after,
        SourceProcessingStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId && s.CreatedAt > after);

        if (status is not null)
        {
            query = query.Where(s => s.ProcessingStatus == status.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<SourceProcessingStatus, int>> CountByStatusAsync(
        Guid worldId,
        Guid requestingUserId,
        WorldRole role,
        CancellationToken cancellationToken = default)
    {
        // The visibility predicate is the shared expression, so this cannot drift from the
        // in-memory filter that produces the list these counts describe.
        var counts = await _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId)
            .Where(SourceVisibilityRule.CanSee(requestingUserId, role))
            .GroupBy(s => s.ProcessingStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.Status, c => c.Count);
    }

    public async Task<IReadOnlyList<SourceAttribution>> ListAttributionByIdsAsync(
        IReadOnlyList<Guid> ids,
        Guid userId,
        WorldRole role,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        // Projected, not Include'd: this deliberately never touches Body or DerivedText.
        return await _context.Sources
            .AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Where(SourceVisibilityRule.CanSee(userId, role))
            .Select(s => new SourceAttribution(s.Id, s.Title, s.Visibility, s.CreatedByUserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.Sources
            .AsNoTracking()
            .Include(s => s.Campaign)
            .Where(s => s.WorldId == worldId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListRecentSessionsAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return await _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId
                && (SessionTypes.Contains(s.Type)
                    || (s.Type == SourceType.ImportedNote && s.OccurredAt != null)))
            .Where(SourceVisibilityRule.CanSee(userId, role))
            .OrderByDescending(s => s.OccurredAt ?? s.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListTimelineBeforeAsync(
        Guid worldId,
        Guid? campaignId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        // Undated imported notes participate by CreatedAt — for a bulk import that is
        // upload order, the best available approximation of story order. The strict
        // tuple comparison (effective date, then CreatedAt) also excludes the pivot
        // source itself.
        return await _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId
                && (SessionTypes.Contains(s.Type) || s.Type == SourceType.ImportedNote)
                && (campaignId == null || s.CampaignId == null || s.CampaignId == campaignId)
                && ((s.OccurredAt ?? s.CreatedAt) < pivotOccurred
                    || ((s.OccurredAt ?? s.CreatedAt) == pivotOccurred && s.CreatedAt < pivotCreated)))
            .Where(filter.CanSeeSource())
            .OrderByDescending(s => s.OccurredAt ?? s.CreatedAt)
            .ThenByDescending(s => s.CreatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListExtractableAfterAsync(
        Guid worldId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return await ExtractableAfter(worldId, pivotOccurred, pivotCreated)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountExtractableAfterAsync(
        Guid worldId,
        DateTimeOffset pivotOccurred,
        DateTimeOffset pivotCreated,
        CancellationToken cancellationToken = default)
    {
        return await ExtractableAfter(worldId, pivotOccurred, pivotCreated)
            .CountAsync(cancellationToken);
    }

    /// <summary>The replay queue predicate: every extractable source in a reprocessable
    /// state, strictly after the pivot tuple, earliest first. Ordering mirrors the lookback
    /// in <see cref="ListTimelineBeforeAsync"/> with the direction flipped, but the type
    /// filter is deliberately absent — a replay re-extracts the whole world, and GM notes,
    /// lore documents, uploads and maps carry knowledge too. ExtractionEnabled is the
    /// switch for opting a source out; its type is not.</summary>
    private IQueryable<Source> ExtractableAfter(
        Guid worldId, DateTimeOffset pivotOccurred, DateTimeOffset pivotCreated)
    {
        return _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId
                && s.ExtractionEnabled
                && (s.ProcessingStatus == SourceProcessingStatus.Processed
                    || s.ProcessingStatus == SourceProcessingStatus.Failed)
                && ((s.OccurredAt ?? s.CreatedAt) > pivotOccurred
                    || ((s.OccurredAt ?? s.CreatedAt) == pivotOccurred && s.CreatedAt > pivotCreated)))
            .OrderBy(s => s.OccurredAt ?? s.CreatedAt)
            .ThenBy(s => s.CreatedAt);
    }

    /// <summary>Source types that record a play session (SessionNote plus the legacy
    /// transcript forms) — what "last session" means to the Loremaster. ImportedNote
    /// counts only when dated: the bulk importer stamps OccurredAt on session folders
    /// but not on wiki/character/lore notes.</summary>
    private static readonly SourceType[] SessionTypes =
        [SourceType.SessionNote, SourceType.Transcript, SourceType.SessionAudio];

    public Task UpdateProcessingStatusAsync(Guid id, SourceProcessingStatus status, CancellationToken cancellationToken = default) =>
        MutateAsync(id, source => source.ProcessingStatus = status, cancellationToken);

    /// <summary>The reveal path lifts a GM-only source to PartyVisible without a whole-entity
    /// update, which would fight the general update's post-extraction visibility lock.</summary>
    public Task UpdateVisibilityAsync(Guid id, VisibilityScope visibility, CancellationToken cancellationToken = default) =>
        MutateAsync(id, source => source.Visibility = visibility, cancellationToken);

    /// <summary>The worker persists a vision transcription without clobbering other columns —
    /// the GM may have edited the title while the page images were being read.</summary>
    public Task UpdateBodyAsync(Guid id, string body, CancellationToken cancellationToken = default) =>
        MutateAsync(id, source => source.Body = body, cancellationToken);

    public Task UpdateDerivedTextAsync(Guid id, string? derivedText, CancellationToken cancellationToken = default) =>
        MutateAsync(id, source => source.DerivedText = derivedText, cancellationToken);

    public async Task<bool> TryClaimForExtractionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Not MutateAsync: that one loads, mutates and saves, which is the read-then-write
        // window two workers can both pass through. The predicate here *is* the lock — one
        // UPDATE ... WHERE decides the winner inside the database, exactly as in
        // WorldRepository.TryClaimContinuityAuditAsync. Losing costs nothing; winning twice
        // costs two full extractions and two batches for one source.
        if (_context.Database.IsRelational())
        {
            var affected = await _context.Sources
                .Where(s => s.Id == id && s.ProcessingStatus == SourceProcessingStatus.Queued)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(s => s.ProcessingStatus, SourceProcessingStatus.Processing)
                        // ExecuteUpdate bypasses the change tracker, so the DbContext's stamp
                        // never sees this one. Missing it would leave a source that is genuinely
                        // Processing wearing the timestamp of when it was Queued.
                        .SetProperty(s => s.StatusChangedAt, DateTimeOffset.UtcNow),
                    cancellationToken);

            return affected == 1;
        }

        // InMemory (API integration tests) has no ExecuteUpdate. Single-threaded there, so a
        // read-modify-write reproduces the observable contract without the atomicity.
        var source = await _context.Sources.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (source is null || source.ProcessingStatus != SourceProcessingStatus.Queued)
        {
            return false;
        }

        source.ProcessingStatus = SourceProcessingStatus.Processing;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Loads the row tracked, applies one column, saves. Four scoped writers were this same
    /// eleven lines with one assignment changed; the differences that mattered were the
    /// assignment and the reason, so those are what is left at each call site.
    ///
    /// Tracked rather than <c>ExecuteUpdate</c> deliberately: these run inside the unit of
    /// work alongside other writes, and a bulk statement would commit outside it.
    /// </summary>
    private async Task MutateAsync(Guid id, Action<Source> apply, CancellationToken cancellationToken)
    {
        var source = await _context.LoadForUpdateAsync<Source>(id, cancellationToken);
        apply(source);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(source, cancellationToken);
        await LoadCampaignAsync(source, cancellationToken);
        return source;
    }

    /// <summary>
    /// Keeps the Campaign navigation in sync with CampaignId after a write, so responses
    /// mapped from the returned entity carry the (current) campaign name.
    /// </summary>
    private async Task LoadCampaignAsync(Source source, CancellationToken cancellationToken)
    {
        if (source.Campaign?.Id == source.CampaignId)
        {
            return;
        }

        // The navigation is stale (campaign changed or cleared). Drop it and, when a
        // campaign is set, reload it from the context.
        source.Campaign = null;

        if (source.CampaignId is not null)
        {
            await _context.Entry(source).Reference(s => s.Campaign).LoadAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Tracked rather than <c>DeleteWhereAsync</c>: the ledger detach below and the row
    /// removal have to land in one <c>SaveChanges</c>, or a failure between them leaves the
    /// spend history orphaned from a source that still exists.
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await _context.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (source is null)
        {
            return;
        }

        // The cost ledger outlives the source it references (its FK is NoAction by
        // design) — detach the link instead of losing the spend history.
        var usageRecords = await _context.AiUsageRecords
            .Where(u => u.SourceId == id)
            .ToListAsync(cancellationToken);
        foreach (var record in usageRecords)
        {
            record.SourceId = null;
        }

        _context.Sources.Remove(source);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
