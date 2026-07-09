using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorldMemberRepository : IWorldMemberRepository
{
    private readonly NornisDbContext _context;

    public WorldMemberRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<WorldMember> CreateAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        _context.WorldMembers.Add(member);
        await _context.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<WorldMember?> GetByWorldAndUserAsync(Guid worldId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.WorldMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(cm => cm.WorldId == worldId && cm.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<WorldMember>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.WorldMembers
            .AsNoTracking()
            .Include(cm => cm.User)
            .Where(cm => cm.WorldId == worldId)
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        _context.WorldMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorldMember> UpdateAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        _context.WorldMembers.Update(member);
        await _context.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<int> CountByRoleAsync(Guid worldId, WorldRole role, CancellationToken cancellationToken = default)
    {
        return await _context.WorldMembers
            .CountAsync(cm => cm.WorldId == worldId && cm.Role == role, cancellationToken);
    }

    public async Task<IReadOnlyList<WorldMember>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.WorldMembers
            .AsNoTracking()
            .Where(cm => cm.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
