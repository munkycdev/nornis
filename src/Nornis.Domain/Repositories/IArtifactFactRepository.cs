using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IArtifactFactRepository
{
    Task<ArtifactFact> CreateAsync(ArtifactFact fact, CancellationToken cancellationToken = default);

    Task<ArtifactFact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactFact>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<ArtifactFact> UpdateAsync(ArtifactFact fact, CancellationToken cancellationToken = default);

    /// <summary>
    /// Newest facts per artifact, limited to the given visibility scopes. The scope filter
    /// applies before <paramref name="maxPerArtifact"/>, so out-of-scope facts never consume
    /// cap slots. Scopes are required — every caller must decide what the reader may see
    /// (mirrors <see cref="IArtifactRelationshipRepository.ListByArtifactIdsAsync"/>).
    /// </summary>
    Task<IReadOnlyList<ArtifactFact>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int maxPerArtifact,
        CancellationToken cancellationToken = default);
}
