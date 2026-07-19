using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IArtifactRepository
{
    Task<Artifact> CreateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task<Artifact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> ListByWorldAsync(Guid worldId, ArtifactType? type = null, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    Task<Artifact> UpdateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes an artifact. Facts cascade at the database level; callers must ensure
    /// no relationships remain (their FK is Restrict) and clear character links first.
    /// </summary>
    Task DeleteAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exact (case-insensitive) name match within a world. Returns all matches so
    /// callers can detect ambiguous names.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByExactNameAsync(Guid worldId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// All non-archived artifacts of one type visible to the reader — e.g. the world's
    /// Locations as matching context for map extraction.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByTypeAsync(
        Guid worldId,
        ArtifactType type,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recent artifacts for AI context/retrieval, limited to what the reader may see.
    /// Excludes Archived artifacts — they are merge leftovers and must not re-enter
    /// extraction or ask context.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListRecentByWorldAsync(
        Guid worldId,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Artifacts whose names appear in the given text, for AI context/retrieval,
    /// limited to what the reader may see. Excludes Archived artifacts — they are
    /// merge leftovers and must not re-enter extraction or ask context.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(
        Guid worldId,
        string text,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default);
}
