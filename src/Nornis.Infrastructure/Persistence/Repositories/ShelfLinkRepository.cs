using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ShelfLinkRepository : IShelfLinkRepository
{
    private readonly NornisDbContext _context;

    public ShelfLinkRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ShelfLink> CreateAsync(ShelfLink link, CancellationToken cancellationToken = default)
    {
        _context.ShelfLinks.Add(link);
        await _context.SaveChangesAsync(cancellationToken);
        _context.Entry(link).State = EntityState.Detached;
        return link;
    }

    public async Task<ShelfLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        return await _context.ShelfLinks
            .AsNoTracking()
            .Include(l => l.Player)
                .ThenInclude(p => p.World)
            .FirstOrDefaultAsync(l => l.Code == code, cancellationToken);
    }

    public async Task<ShelfLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ShelfLinks
            .AsNoTracking()
            .Include(l => l.Player)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<ShelfLink?> GetActiveByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return await _context.ShelfLinks
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.PlayerId == playerId && l.RevokedAt == null, cancellationToken);
    }

    public async Task<IReadOnlyList<ShelfLink>> ListActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ShelfLinks
            .AsNoTracking()
            .Where(l => l.RevokedAt == null && l.Player.WorldId == worldId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken = default) =>
        _context.SetWhereAsync<ShelfLink, DateTimeOffset?>(
            l => l.Id == id && l.RevokedAt == null, l => l.RevokedAt, revokedAt, cancellationToken);

    public Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default) =>
        _context.SetWhereAsync<ShelfLink, DateTimeOffset?>(
            l => l.Id == id, l => l.LastUsedAt, usedAt, cancellationToken);
}
