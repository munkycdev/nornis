using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorldRepository : IWorldRepository
{
    private readonly NornisDbContext _context;

    public WorldRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<World> CreateAsync(World world, CancellationToken cancellationToken = default)
    {
        _context.Worlds.Add(world);
        await _context.SaveChangesAsync(cancellationToken);
        return world;
    }

    public async Task<World?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Worlds
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default)
    {
        _context.Worlds.Update(world);
        await _context.SaveChangesAsync(cancellationToken);
        return world;
    }

    public async Task<IReadOnlyList<World>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Worlds
            .AsNoTracking()
            .Where(c => _context.WorldMembers.Any(cm => cm.WorldId == c.Id && cm.UserId == userId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<World>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await _context.Worlds
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
    }
}
