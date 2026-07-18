using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface ISourceRepository
{
    Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default);

    Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    /// <summary>Scoped Body write — used by the worker to persist a vision transcription.</summary>
    Task UpdateBodyAsync(Guid id, string body, CancellationToken cancellationToken = default);

    /// <summary>Scoped DerivedText write — the worker persists derived attachment text
    /// before extracting (so redelivery never re-buys it); the attachment service clears
    /// it (null) when derivation inputs change.</summary>
    Task UpdateDerivedTextAsync(Guid id, string? derivedText, CancellationToken cancellationToken = default);

    Task UpdateProcessingStatusAsync(Guid id, SourceProcessingStatus status, CancellationToken cancellationToken = default);

    Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
