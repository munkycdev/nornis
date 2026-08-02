using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IReviewBatchRepository
{
    Task<ReviewBatch> CreateAsync(ReviewBatch batch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the one extraction batch a source is allowed, or returns null when another run
    /// already committed it. Separate from <see cref="CreateAsync"/> because it is a different
    /// verb: a conditional insert whose condition the database enforces, for the only batch
    /// kind that is one-per-source. Every other batch kind is free to repeat and keeps using
    /// <see cref="CreateAsync"/>.
    /// </summary>
    Task<ReviewBatch?> TryCreateExtractionBatchAsync(ReviewBatch batch, CancellationToken cancellationToken = default);

    Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The batches for the given ids, one query. Unknown ids are simply absent.</summary>
    Task<IReadOnlyList<ReviewBatch>> ListByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    Task<ReviewBatch?> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>All batches for a source, extraction and backfill kinds alike.</summary>
    Task<IReadOnlyList<ReviewBatch>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Whether a batch of the given kind exists for the source (sweep idempotency).</summary>
    Task<bool> ExistsForSourceAsync(Guid sourceId, string kind, CancellationToken cancellationToken = default);

    /// <summary>Deletes all of a source's batches (proposals cascade). The batch→source FK
    /// is Restrict (SQL Server cascade-path limits), so source deletion clears these first.</summary>
    Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewBatch>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the world has any batch of the given kind. Backs the onboarding checklist's
    /// "reveal a secret" step, which is polled every 15 seconds and previously answered by
    /// loading every batch in the world.
    /// </summary>
    Task<bool> AnyOfKindAsync(Guid worldId, string kind, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, ReviewBatchStatus status, CancellationToken cancellationToken = default);

    Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default);
}
