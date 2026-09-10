using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IPlayerRepository
{
    Task<Player> CreateAsync(Player player, CancellationToken cancellationToken = default);

    Task<Player?> GetByIdAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>The player linked to a membership, which every membership has exactly one of.</summary>
    Task<Player?> GetByMemberAsync(Guid worldMemberId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The member's linked player, made if missing. Every membership is created with one and
    /// the migration gave one to every membership that predates them, so a missing row is a
    /// defect — and the repair is the row the factory would have made, named as the member is.
    /// </summary>
    Task<Player> GetOrCreateByMemberAsync(WorldMember member, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Player>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<Player> UpdateAsync(Player player, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-points every character of <paramref name="fromPlayerId"/> to <paramref name="toPlayerId"/>
    /// and deletes the emptied player, in one save. One operation so that conservation — the
    /// world's set of characters is the same before and after — is a transaction, not a loop
    /// that can stop halfway.
    /// </summary>
    Task MoveCharactersAndDeleteAsync(Guid fromPlayerId, Guid toPlayerId, CancellationToken cancellationToken = default);
}
