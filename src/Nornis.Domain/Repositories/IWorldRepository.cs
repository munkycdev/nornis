using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IWorldRepository
{
    Task<World> CreateAsync(World world, CancellationToken cancellationToken = default);

    Task<World?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Case-insensitive public-slug lookup (slugs are stored lowercase).</summary>
    Task<World?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<World>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<World>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes a world and every row that belongs to it — members, invites,
    /// campaigns, characters, sources, knowledge, reviews, library, health, replays, and
    /// the world's AI usage ledger. Irreversible; blob cleanup is the caller's job.
    /// </summary>
    Task DeleteAsync(Guid worldId, CancellationToken cancellationToken = default);
}
