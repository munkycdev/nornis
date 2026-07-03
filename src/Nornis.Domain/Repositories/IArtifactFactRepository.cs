using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IArtifactFactRepository
{
    Task<ArtifactFact> CreateAsync(ArtifactFact fact, CancellationToken cancellationToken = default);

    Task<ArtifactFact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactFact>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<ArtifactFact> UpdateAsync(ArtifactFact fact, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactFact>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        int maxPerArtifact,
        CancellationToken cancellationToken = default);
}
