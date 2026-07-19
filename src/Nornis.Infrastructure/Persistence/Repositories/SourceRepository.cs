using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
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
