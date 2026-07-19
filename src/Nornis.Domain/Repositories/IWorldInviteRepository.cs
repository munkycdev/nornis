using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IWorldInviteRepository
{
    Task<WorldInvite> CreateAsync(WorldInvite invite, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up an invite by its redemption code, including the owning <see cref="World"/> so
    /// callers can show its name. Tracked, because redemption mutates the invite.
    /// </summary>
    Task<WorldInvite?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<WorldInvite?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldInvite>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<WorldInvite> UpdateAsync(WorldInvite invite, CancellationToken cancellationToken = default);
}
