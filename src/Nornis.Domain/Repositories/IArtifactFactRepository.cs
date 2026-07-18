using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IArtifactFactRepository
{
    Task<ArtifactFact> CreateAsync(ArtifactFact fact, CancellationToken cancellationToken = default);

    Task<ArtifactFact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactFact>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<ArtifactFact> UpdateAsync(ArtifactFact fact, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid factId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Newest facts per artifact, limited to what the reader may see. The visibility
    /// filter applies before <paramref name="maxPerArtifact"/>, so invisible facts never
    /// consume cap slots. The filter is required — every caller must decide what the
    /// reader may see (mirrors <see cref="IArtifactRelationshipRepository.ListByArtifactIdsAsync"/>).
    /// </summary>
    Task<IReadOnlyList<ArtifactFact>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        VisibilityFilter filter,
        int maxPerArtifact,
        CancellationToken cancellationToken = default);
}
