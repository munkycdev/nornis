using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Hydrates the <see cref="ShelfLink.Player"/> and <see cref="Player.World"/> navigations from
/// the sibling fakes on read, the way the EF repository's Includes do — the service names the
/// world and the player from the link alone.
/// </summary>
public class InMemoryShelfLinkRepository : IShelfLinkRepository
{
    private readonly List<ShelfLink> _links = [];
    private readonly InMemoryPlayerRepository _players;
    private readonly InMemoryWorldRepository _worlds;

    public InMemoryShelfLinkRepository(InMemoryPlayerRepository players, InMemoryWorldRepository worlds)
    {
        _players = players;
        _worlds = worlds;
    }

    public IReadOnlyList<ShelfLink> Links => _links.AsReadOnly();

    public Task<ShelfLink> CreateAsync(ShelfLink link, CancellationToken cancellationToken = default)
    {
        _links.Add(link);
        return Task.FromResult(link);
    }

    public Task<ShelfLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default) =>
        Task.FromResult(Hydrate(_links.FirstOrDefault(l => l.Code == code)));

    public Task<ShelfLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Hydrate(_links.FirstOrDefault(l => l.Id == id)));

    public Task<ShelfLink?> GetActiveByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_links.FirstOrDefault(l => l.PlayerId == playerId && l.RevokedAt is null));

    public Task<IReadOnlyList<ShelfLink>> ListActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var playerIds = _players.Players.Where(p => p.WorldId == worldId).Select(p => p.Id).ToHashSet();
        var links = _links
            .Where(l => l.RevokedAt is null && playerIds.Contains(l.PlayerId))
            .OrderByDescending(l => l.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<ShelfLink>>(links);
    }

    public Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken = default)
    {
        foreach (var link in _links.Where(l => l.Id == id && l.RevokedAt is null))
        {
            link.RevokedAt = revokedAt;
        }
        return Task.CompletedTask;
    }

    public Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default)
    {
        foreach (var link in _links.Where(l => l.Id == id))
        {
            link.LastUsedAt = usedAt;
        }
        return Task.CompletedTask;
    }

    private ShelfLink? Hydrate(ShelfLink? link)
    {
        if (link is null)
        {
            return null;
        }

        var player = _players.Players.First(p => p.Id == link.PlayerId);
        player.World = _worlds.Worlds.First(w => w.Id == player.WorldId);
        link.Player = player;
        return link;
    }
}
