using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ReviewBatchRepository : IReviewBatchRepository
{
    private readonly NornisDbContext _context;

    public ReviewBatchRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ReviewBatch> CreateAsync(ReviewBatch batch, CancellationToken cancellationToken = default)
    {
        _context.ReviewBatches.Add(batch);
        await _context.SaveChangesAsync(cancellationToken);
        return batch;
    }

    public async Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(rb => rb.Id == id, cancellationToken);
    }

    public async Task<ReviewBatch?> GetBySourceIdAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        // Kind == null keeps this the *extraction* batch: sweep batches (e.g. the
        // relationship backfill) also live on the source but must not satisfy
        // extraction's one-batch-per-source idempotency check.
        return await _context.ReviewBatches
            .AsNoTracking()
            .Where(rb => rb.SourceId == sourceId
                && rb.Kind == null
                && (rb.Status == ReviewBatchStatus.Pending
                    || rb.Status == ReviewBatchStatus.InReview
                    || rb.Status == ReviewBatchStatus.Completed))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> ExistsForSourceAsync(Guid sourceId, string kind, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .AnyAsync(rb => rb.SourceId == sourceId && rb.Kind == kind, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewBatch>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .Where(rb => rb.WorldId == worldId)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid id, ReviewBatchStatus status, CancellationToken cancellationToken = default)
    {
        var batch = await _context.ReviewBatches
            .FirstAsync(rb => rb.Id == id, cancellationToken);

        batch.Status = status;

        if (status == ReviewBatchStatus.Completed)
        {
            batch.CompletedAt = DateTimeOffset.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        var batch = await _context.ReviewBatches
            .FirstAsync(rb => rb.Id == id, cancellationToken);

        batch.Status = ReviewBatchStatus.Completed;
        batch.CompletedAt = completedAt;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
