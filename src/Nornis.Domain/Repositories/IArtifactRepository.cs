using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IArtifactRepository
{
    Task<Artifact> CreateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task<Artifact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> ListByWorldAsync(Guid worldId, ArtifactType? type = null, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    Task<Artifact> UpdateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> SearchByNameAsync(Guid worldId, string searchTerm, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exact (case-insensitive) name match within a world. Returns all matches so
    /// callers can detect ambiguous names.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByExactNameAsync(Guid worldId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent artifacts for AI context/retrieval. Excludes Archived artifacts —
    /// they are merge leftovers and must not re-enter extraction or ask context.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListRecentByWorldAsync(
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Artifacts whose names appear in the given text, for AI context/retrieval.
    /// Excludes Archived artifacts — they are merge leftovers and must not
    /// re-enter extraction or ask context.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(
        Guid worldId,
        string text,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default);
}
