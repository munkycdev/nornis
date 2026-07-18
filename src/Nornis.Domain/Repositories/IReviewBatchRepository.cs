using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IReviewBatchRepository
{
    Task<ReviewBatch> CreateAsync(ReviewBatch batch, CancellationToken cancellationToken = default);

    Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ReviewBatch?> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>All batches for a source, extraction and backfill kinds alike.</summary>
    Task<IReadOnlyList<ReviewBatch>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Whether a batch of the given kind exists for the source (sweep idempotency).</summary>
    Task<bool> ExistsForSourceAsync(Guid sourceId, string kind, CancellationToken cancellationToken = default);

    /// <summary>Deletes all of a source's batches (proposals cascade). The batch→source FK
    /// is Restrict (SQL Server cascade-path limits), so source deletion clears these first.</summary>
    Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewBatch>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, ReviewBatchStatus status, CancellationToken cancellationToken = default);

    Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
}
