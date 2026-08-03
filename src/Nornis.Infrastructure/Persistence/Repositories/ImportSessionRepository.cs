using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ImportSessionRepository : IImportSessionRepository
{
    private readonly NornisDbContext _context;

    public ImportSessionRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ImportSession> CreateAsync(ImportSession session, CancellationToken cancellationToken = default)
    {
        _context.ImportSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<ImportSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ImportSessions
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<ImportSession?> GetNonTerminalByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ImportSessions
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(
                s => s.WorldId == worldId
                    && (s.Status == ImportSessionStatus.Draft || s.Status == ImportSessionStatus.InProgress),
                cancellationToken);
    }

    public async Task UpdateAsync(
        Guid id, ImportSessionStatus status, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var session = await _context.LoadForUpdateAsync<ImportSession>(id, cancellationToken);
        session.Status = status;
        session.UpdatedAt = updatedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task TouchAsync(Guid id, DateTimeOffset updatedAt, CancellationToken cancellationToken = default)
    {
        var session = await _context.LoadForUpdateAsync<ImportSession>(id, cancellationToken);
        session.UpdatedAt = updatedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ImportSessionItem> AddItemAsync(ImportSessionItem item, CancellationToken cancellationToken = default)
    {
        _context.ImportSessionItems.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task AddItemsAsync(
        IReadOnlyList<ImportSessionItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0)
        {
            return;
        }

        _context.ImportSessionItems.AddRange(items);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetItemDispatchedAsync(
        Guid itemId, DateTimeOffset dispatchedAt, CancellationToken cancellationToken = default)
    {
        var item = await _context.LoadForUpdateAsync<ImportSessionItem>(itemId, cancellationToken);
        item.DispatchedAt = dispatchedAt;
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task DeleteItemAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        _context.DeleteWhereAsync<ImportSessionItem>(i => i.Id == itemId, cancellationToken);

    public async Task SetItemPositionsAsync(
        IReadOnlyList<(Guid ItemId, int Position)> positions, CancellationToken cancellationToken = default)
    {
        if (positions.Count == 0)
        {
            return;
        }

        // Positions are unique per session but the reorder rewrites them as a set, so a
        // one-shot tracked save is both correct and cheaper than a statement per row.
        var ids = positions.Select(p => p.ItemId).ToList();
        var items = await _context.ImportSessionItems
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var wanted = positions.ToDictionary(p => p.ItemId, p => p.Position);
        foreach (var item in items)
        {
            item.Position = wanted[item.Id];
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetItemSkippedAsync(Guid itemId, bool skipped, CancellationToken cancellationToken = default)
    {
        var item = await _context.LoadForUpdateAsync<ImportSessionItem>(itemId, cancellationToken);
        item.Skipped = skipped;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
