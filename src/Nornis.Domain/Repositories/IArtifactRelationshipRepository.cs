using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IArtifactRelationshipRepository
{
    Task<ArtifactRelationship> CreateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default);

    Task<ArtifactRelationship?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default);

    Task<ArtifactRelationship> UpdateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default);
}
