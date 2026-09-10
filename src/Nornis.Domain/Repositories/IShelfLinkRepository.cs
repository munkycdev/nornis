using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IShelfLinkRepository
{
    Task<ShelfLink> CreateAsync(ShelfLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// The link behind a code, with its <see cref="Player"/> and the player's <see cref="World"/>,
    /// so the anonymous shelf can name both without a second lookup. Read-only.
    /// </summary>
    Task<ShelfLink?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>With its <see cref="Player"/>, so a GM operation can check the world. Read-only.</summary>
    Task<ShelfLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The player's standing link, of which there is at most one.</summary>
    Task<ShelfLink?> GetActiveByPlayerAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Every standing link in a world, through the players that own them.</summary>
    Task<IReadOnlyList<ShelfLink>> ListActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>Stamps <see cref="ShelfLink.RevokedAt"/> on a standing link. A revoked one is left as it is.</summary>
    Task RevokeAsync(Guid id, DateTimeOffset revokedAt, CancellationToken cancellationToken = default);

    /// <summary>Stamps <see cref="ShelfLink.LastUsedAt"/>.</summary>
    Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken = default);
}
