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

    /// <summary>Deletes all of a source's references. Used when a source is edited and
    /// reprocessed: the old body's quotes and derivation trail no longer apply.</summary>
    Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every reference pointing at one target entity. Used when an
    /// artifact/fact/relationship is removed from canon so its provenance rows don't dangle.</summary>
    Task DeleteByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default);
}
