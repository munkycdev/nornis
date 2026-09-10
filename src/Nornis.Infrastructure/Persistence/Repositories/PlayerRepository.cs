using Microsoft.EntityFrameworkCore;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class PlayerRepository : IPlayerRepository
{
    private readonly NornisDbContext _context;

    public PlayerRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Player> CreateAsync(Player player, CancellationToken cancellationToken = default)
    {
        _context.Players.Add(player);
        await _context.SaveChangesAsync(cancellationToken);
        return player;
    }

    public async Task<Player?> GetByIdAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
    }

    public async Task<Player?> GetByMemberAsync(Guid worldMemberId, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.WorldMemberId == worldMemberId, cancellationToken);
    }

    public async Task<Player> GetOrCreateByMemberAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        var existing = await GetByMemberAsync(member.Id, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var now = DateTimeOffset.UtcNow;
        return await CreateAsync(new Player
        {
            Id = Guid.NewGuid(),
            WorldId = member.WorldId,
            WorldMemberId = member.Id,
            Name = MemberDisplayName.For(member),
            CreatedAt = now,
            UpdatedAt = now,
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<Player>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .AsNoTracking()
            .Where(p => p.WorldId == worldId)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Player> UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(player, cancellationToken);
        return player;
    }

    public async Task DeleteAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        await _context.DeleteWhereAsync<Player>(p => p.Id == playerId, cancellationToken);
    }

    public async Task MoveCharactersAndDeleteAsync(Guid fromPlayerId, Guid toPlayerId, CancellationToken cancellationToken = default)
    {
        // Tracked, so the re-pointing and the delete leave in one SaveChanges — the
        // in-memory provider has no ExecuteUpdate, and one save is the transaction either way.
        var characters = await _context.Characters
            .Where(c => c.PlayerId == fromPlayerId)
            .ToListAsync(cancellationToken);

        foreach (var character in characters)
        {
            character.PlayerId = toPlayerId;
        }

        var player = await _context.Players.FirstOrDefaultAsync(p => p.Id == fromPlayerId, cancellationToken);
        if (player is not null)
        {
            _context.Players.Remove(player);
        }

        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }
}
