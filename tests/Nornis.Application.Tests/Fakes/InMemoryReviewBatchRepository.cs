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

    public Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var batch = _batches.FirstOrDefault(b => b.Id == id);
        return Task.FromResult(batch);
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
        var batch = _batches.FirstOrDefault(b => b.Id == id);
        if (batch is not null)
        {
            batch.Status = status;
        }
        return Task.CompletedTask;
    }

    public Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        var batch = _batches.FirstOrDefault(b => b.Id == id);
        if (batch is not null)
        {
            batch.Status = ReviewBatchStatus.Completed;
            batch.CompletedAt = completedAt;
        }
        return Task.CompletedTask;
    }
}
