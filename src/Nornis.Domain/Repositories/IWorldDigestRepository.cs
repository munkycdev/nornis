using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IWorldDigestRepository
{
    Task<WorldDigest?> GetByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the world's digest — the row is the record, one per world. Two GMs
    /// generating at once is a last-write-wins race on a regenerable read-model; the
    /// loser's spend is wasted, nothing is corrupted.
    /// </summary>
    Task UpsertAsync(WorldDigest digest, CancellationToken cancellationToken = default);
}
