using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IArtifactRepository
{
    Task<Artifact> CreateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    Task<Artifact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Artifact>> ListByWorldAsync(Guid worldId, ArtifactType? type = null, CancellationToken cancellationToken = default);

    Task<Artifact> UpdateAsync(Artifact artifact, CancellationToken cancellationToken = default);

    /// <summary>
    /// The summary refresh's scoped write: the regenerated text (null: leave the text —
    /// the review route stamps without writing) and the SummaryRefreshedAt provenance
    /// stamp, without clobbering columns a concurrent accept may be editing.
    /// </summary>
    Task UpdateSummaryAsync(Guid id, string? summary, DateTimeOffset refreshedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-deletes an artifact. Facts cascade at the database level; callers must ensure
    /// no relationships remain (their FK is Restrict) and clear character links first.
    /// </summary>
    Task DeleteAsync(Guid artifactId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Name match within a world under <see cref="ArtifactNameKey"/> — trim, collapse internal
    /// whitespace, ignore case — restricted to non-archived artifacts the reader may see.
    /// Returns all visible matches so callers can detect ambiguous names.
    ///
    /// The equivalence policy is deliberately shared with apply-time dedup: if resolution were
    /// stricter than matching, a create could bind to an existing artifact while every fact in
    /// the same batch that referenced it by name failed to resolve, with no way to recover.
    ///
    /// The filter is required rather than optional: an unfiltered name lookup is an existence
    /// oracle over the world's whole artifact table, so callers must state the reader whose
    /// eyes they are resolving through (<see cref="VisibilityFilter.All"/> for GM-gated
    /// internal work).
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByEquivalentNameAsync(Guid worldId, string name, VisibilityFilter filter, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Artifacts by their own ids, unfiltered (mirrors <see cref="GetByIdAsync"/>) — for
    /// GM-gated internal work such as resolving continuity-finding evidence refs. Missing ids
    /// are silently absent from the result.
    /// </summary>
    Task<IReadOnlyList<Artifact>> ListByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default);
}
