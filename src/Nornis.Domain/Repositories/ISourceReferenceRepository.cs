using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface ISourceReferenceRepository
{
    Task<SourceReference> CreateAsync(SourceReference reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceReference>> ListByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceReference>> ListByTargetIdsAsync(IReadOnlyList<Guid> targetIds, CancellationToken cancellationToken = default);

    /// <summary>All references produced by a source — the provenance trail of its extraction.</summary>
    Task<IReadOnlyList<SourceReference>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>References produced by any of the given sources, in one query — the batch
    /// sibling of <see cref="ListBySourceAsync"/> for read models that walk many sources.</summary>
    Task<IReadOnlyList<SourceReference>> ListBySourceIdsAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default);

    /// <summary>How many references each of the given sources has produced, keyed by source.
    /// Sources with none are absent from the result.
    ///
    /// This is the honest answer to "has this source contributed anything to canon" —
    /// ProcessingStatus is not, because a source whose knowledge was deleted still reads as
    /// Processed. Counting rather than listing keeps a staging screen off a table that runs
    /// to thousands of rows per world.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountBySourcesAsync(IReadOnlyList<Guid> sourceIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes all of a source's references. Used when a source is edited and
    /// reprocessed: the old body's quotes and derivation trail no longer apply.</summary>
    Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every reference pointing at one target entity. Used when an
    /// artifact/fact/relationship is removed from canon so its provenance rows don't dangle.</summary>
    Task DeleteByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the reference(s) from one source to one target — a single unlink, e.g. a
    /// user removing a session's manual link to a Location. A no-op when no such reference exists.</summary>
    Task DeleteBySourceAndTargetAsync(Guid sourceId, SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default);
}
