using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryWorldDigestRepository : IWorldDigestRepository
{
    private readonly Dictionary<Guid, WorldDigest> _byWorld = [];

    public IReadOnlyCollection<WorldDigest> Digests => _byWorld.Values;

    public void Seed(WorldDigest digest) => _byWorld[digest.WorldId] = digest;

    public Task<WorldDigest?> GetByWorldAsync(Guid worldId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_byWorld.GetValueOrDefault(worldId));

    public Task UpsertAsync(WorldDigest digest, CancellationToken cancellationToken = default)
    {
        _byWorld[digest.WorldId] = digest;
        return Task.CompletedTask;
    }
}
