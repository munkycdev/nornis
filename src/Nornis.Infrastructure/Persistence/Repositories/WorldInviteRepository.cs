using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Exceptions;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorldInviteRepository : IWorldInviteRepository
{
    private readonly NornisDbContext _context;

    public WorldInviteRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<WorldInvite> CreateAsync(WorldInvite invite, CancellationToken cancellationToken = default)
    {
        _context.WorldInvites.Add(invite);
        await _context.SaveChangesAsync(cancellationToken);
        return invite;
    }

    public async Task<WorldInvite?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        // Tracked (no AsNoTracking): redemption increments UseCount on the returned entity.
        return await _context.WorldInvites
            .Include(i => i.World)
            .FirstOrDefaultAsync(i => i.Code == code, cancellationToken);
    }

    public async Task<WorldInvite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.WorldInvites
            .FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<WorldInvite>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.WorldInvites
            .AsNoTracking()
            .Where(i => i.WorldId == worldId)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorldInvite> UpdateAsync(WorldInvite invite, CancellationToken cancellationToken = default)
    {
        _context.WorldInvites.Update(invite);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Another redemption changed this invite's RowVersion first. Surface a
            // provider-agnostic signal so the application layer can react.
            throw new ConcurrencyConflictException(
                "The invite was modified concurrently.", ex);
        }

        return invite;
    }
}
