using Microsoft.EntityFrameworkCore;
using Nornis.Application.Services;
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

    /// <summary>
    /// Removes the membership and leaves its player at the table: the player is unlinked, not
    /// deleted, and keeps every character. The player takes the member's name as it was at
    /// this moment (the one name rule, <see cref="MemberDisplayName"/>), since that is the
    /// last thing the table knew them as. Done here because the FK cannot SET NULL on its own
    /// (see PlayerConfiguration) and this is the one place a membership is removed.
    /// </summary>
    public async Task RemoveAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        var player = await _context.Players.FirstOrDefaultAsync(p => p.WorldMemberId == member.Id, cancellationToken);
        if (player is not null)
        {
            player.WorldMemberId = null;
            player.Name = MemberDisplayName.For(member);
            player.UpdatedAt = DateTimeOffset.UtcNow;
        }

        _context.WorldMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    public async Task<WorldMember> UpdateAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(member, cancellationToken);
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
