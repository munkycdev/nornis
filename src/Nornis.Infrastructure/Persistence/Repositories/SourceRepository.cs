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
        return await query
            .OrderByDescending(s => s.CreatedAt)
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
            .Select(s => new SourceAttribution(s.Id, s.Title, s.Visibility, s.CreatedByUserId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, VisibilityScope? visibility = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Sources
            .AsNoTracking()
            .Include(s => s.Campaign)
            .Where(s => s.WorldId == worldId);

        if (visibility is not null)
        {
            query = query.Where(s => s.Visibility == visibility.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Source>> ListRecentSessionsAsync(
        Guid worldId,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        // Hoisted locals translate to SQL parameters.
        var scopes = filter.Scopes;
        var owner = filter.PrivateOwnerUserId;

        return await _context.Sources
            .AsNoTracking()
            .Where(s => s.WorldId == worldId
                && (SessionTypes.Contains(s.Type)
                    || (s.Type == SourceType.ImportedNote && s.OccurredAt != null))
                && scopes.Contains(s.Visibility)
                && (s.Visibility != VisibilityScope.Private || owner == null || s.CreatedByUserId == owner))
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
        // Hoisted locals translate to SQL parameters.
        var scopes = filter.Scopes;
        var owner = filter.PrivateOwnerUserId;

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
                    || ((s.OccurredAt ?? s.CreatedAt) == pivotOccurred && s.CreatedAt < pivotCreated))
                && scopes.Contains(s.Visibility)
                && (s.Visibility != VisibilityScope.Private || owner == null || s.CreatedByUserId == owner))
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

    public async Task UpdateProcessingStatusAsync(Guid id, SourceProcessingStatus status, CancellationToken cancellationToken = default)
    {
        var source = await _context.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (source is null)
        {
            throw new InvalidOperationException($"Source with id '{id}' not found.");
        }

        source.ProcessingStatus = status;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateVisibilityAsync(Guid id, VisibilityScope visibility, CancellationToken cancellationToken = default)
    {
        // Scoped column write (same tracked-load pattern as UpdateProcessingStatusAsync): the
        // reveal path lifts a GM-only source to PartyVisible without a whole-entity update.
        var source = await _context.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (source is null)
        {
            throw new InvalidOperationException($"Source with id '{id}' not found.");
        }

        source.Visibility = visibility;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateBodyAsync(Guid id, string body, CancellationToken cancellationToken = default)
    {
        // Scoped column write (same tracked-load pattern as UpdateProcessingStatusAsync):
        // the worker persists a vision transcription without clobbering other columns.
        var source = await _context.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (source is null)
        {
            throw new InvalidOperationException($"Source with id '{id}' not found.");
        }

        source.Body = body;
        await _context.SaveChangesAsync(cancellationToken);
    }
    public async Task UpdateDerivedTextAsync(Guid id, string? derivedText, CancellationToken cancellationToken = default)
    {
        var source = await _context.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (source is null)
        {
            throw new InvalidOperationException($"Source with id '{id}' not found.");
        }

        source.DerivedText = derivedText;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default)
    {
        _context.Sources.Update(source);
        await _context.SaveChangesAsync(cancellationToken);
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

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var source = await _context.Sources
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (source is null)
        {
            throw new InvalidOperationException($"Source with id '{id}' not found.");
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
