using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IWorldRepository
{
    Task<World> CreateAsync(World world, CancellationToken cancellationToken = default);

    Task<World?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<World>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<World>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);
}
