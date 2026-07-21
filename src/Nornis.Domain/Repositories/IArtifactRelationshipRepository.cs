using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IArtifactRelationshipRepository
{
    Task<ArtifactRelationship> CreateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default);

    Task<ArtifactRelationship?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default);

    Task<ArtifactRelationship> UpdateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid relationshipId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Relationships by their own ids, unfiltered (mirrors <see cref="GetByIdAsync"/>) — for
    /// GM-gated internal work such as resolving continuity-finding evidence refs. Missing ids
    /// are silently absent from the result.
    /// </summary>
    Task<IReadOnlyList<ArtifactRelationship>> ListByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);
}
