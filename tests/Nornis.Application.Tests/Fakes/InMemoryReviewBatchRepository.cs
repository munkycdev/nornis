using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryReviewBatchRepository : IReviewBatchRepository
{
    private readonly List<ReviewBatch> _batches = [];

    public IReadOnlyList<ReviewBatch> Batches => _batches.AsReadOnly();

    public Task<ReviewBatch> CreateAsync(ReviewBatch batch, CancellationToken cancellationToken = default)
    {
        _batches.Add(batch);
        return Task.FromResult(batch);
    }

    /// <summary>
    /// Enforces IX_ReviewBatches_SourceId_Extraction. A fake that let a second extraction batch
    /// through would disagree with production about what is possible, and every test asserting
    /// the loser's behaviour would be asserting against a world that cannot exist.
    /// </summary>
    public Task<ReviewBatch?> TryCreateExtractionBatchAsync(
        ReviewBatch batch, CancellationToken cancellationToken = default)
    {
        if (_batches.Any(b => b.SourceId == batch.SourceId
                              && b.Kind is null
                              && b.Status is ReviewBatchStatus.Pending
                                  or ReviewBatchStatus.InReview
                                  or ReviewBatchStatus.Completed))
        {
            return Task.FromResult<ReviewBatch?>(null);
        }

        _batches.Add(batch);
        return Task.FromResult<ReviewBatch?>(batch);
    }

    public Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = _batches.FirstOrDefault(b => b.Id == id);
        return Task.FromResult(batch);
    }

    public Task<IReadOnlyList<ReviewBatch>> ListByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        var result = _batches.Where(b => ids.Contains(b.Id)).ToList();
        return Task.FromResult<IReadOnlyList<ReviewBatch>>(result.AsReadOnly());
    }

    public Task<ReviewBatch?> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        // Mirrors the EF repository: only extraction batches (Kind == null) count.
        var batch = _batches.FirstOrDefault(b =>
            b.SourceId == sourceId &&
            b.Kind == null &&
            b.Status is ReviewBatchStatus.Pending or ReviewBatchStatus.InReview or ReviewBatchStatus.Completed);
        return Task.FromResult(batch);
    }

    public Task<IReadOnlyList<ReviewBatch>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var batches = _batches.Where(b => b.SourceId == sourceId).ToList();
        return Task.FromResult<IReadOnlyList<ReviewBatch>>(batches.AsReadOnly());
    }

    public Task<bool> ExistsForSourceAsync(Guid sourceId, string kind, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_batches.Any(b => b.SourceId == sourceId && b.Kind == kind));
    }

    public Task<bool> AnyOfKindAsync(Guid worldId, string kind, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_batches.Any(b => b.WorldId == worldId && b.Kind == kind));
    }

    public Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        _batches.RemoveAll(b => b.SourceId == sourceId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReviewBatch>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var batches = _batches.Where(b => b.WorldId == worldId).ToList();
        return Task.FromResult<IReadOnlyList<ReviewBatch>>(batches.AsReadOnly());
    }

    public Task UpdateStatusAsync(Guid id, ReviewBatchStatus status, CancellationToken cancellationToken = default)
    {
        Required(id).Status = status;
        return Task.CompletedTask;
    }

    public Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        var batch = Required(id);
        batch.Status = ReviewBatchStatus.Completed;
        batch.CompletedAt = completedAt;
        return Task.CompletedTask;
    }

    /// <summary>
    /// The scoped writers throw on a missing row because the real repository does — see the
    /// missing-row contract on <see cref="IReviewBatchRepository"/>. A fake that quietly
    /// no-ops where production throws is how a service passes its tests and fails in the world.
    /// </summary>
    private ReviewBatch Required(Guid id) =>
        _batches.FirstOrDefault(b => b.Id == id)
            ?? throw new InvalidOperationException($"ReviewBatch with id '{id}' not found.");
}
