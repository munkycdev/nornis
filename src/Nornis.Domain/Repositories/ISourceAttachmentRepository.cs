using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ISourceAttachmentRepository
{
    Task<SourceAttachment> CreateAsync(SourceAttachment attachment, CancellationToken cancellationToken = default);

    Task<SourceAttachment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>All attachments for a source, ordered by Ord then CreatedAt.</summary>
    Task<IReadOnlyList<SourceAttachment>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<SourceAttachment> UpdateAsync(SourceAttachment attachment, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
