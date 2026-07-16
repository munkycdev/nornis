using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface ILibraryDocumentRepository
{
    Task<LibraryDocument> CreateAsync(LibraryDocument document, CancellationToken cancellationToken = default);

    Task<LibraryDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibraryDocument>> ListByWorldAsync(
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default);

    /// <summary>True when the world has at least one Indexed document within the given scopes —
    /// the cheap pre-check that lets Ask skip question embedding entirely.</summary>
    Task<bool> AnyIndexedAsync(
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default);

    Task<LibraryDocument> UpdateAsync(LibraryDocument document, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
