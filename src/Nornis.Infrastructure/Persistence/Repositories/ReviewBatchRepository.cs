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

    public async Task<ReviewBatch?> TryCreateExtractionBatchAsync(
        ReviewBatch batch, CancellationToken cancellationToken = default)
    {
        _context.ReviewBatches.Add(batch);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // IX_ReviewBatches_SourceId_Extraction rejected a second extraction batch for this
            // source. Translated here rather than in the application layer, which references no
            // persistence library and so cannot name DbUpdateException — the same seam
            // ExtractionReplayRepository uses for its Active-replay index.
            _context.ChangeTracker.Clear();
            return null;
        }

        return batch;
    }

    public async Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .FirstOrDefaultAsync(rb => rb.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewBatch>> ListByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];

        return await _context.ReviewBatches
            .AsNoTracking()
            .Where(rb => ids.Contains(rb.Id))
            .ToListAsync(cancellationToken);
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

    public async Task<IReadOnlyList<ReviewBatch>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .Where(rb => rb.SourceId == sourceId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsForSourceAsync(Guid sourceId, string kind, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .AnyAsync(rb => rb.SourceId == sourceId && rb.Kind == kind, cancellationToken);
    }

    public async Task<bool> AnyOfKindAsync(Guid worldId, string kind, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .AnyAsync(rb => rb.WorldId == worldId && rb.Kind == kind, cancellationToken);
    }

    /// <summary>
    /// Tracked rather than <c>DeleteWhereAsync</c>: the ledger detach below and the batch
    /// removal have to land in one <c>SaveChanges</c>, or a failure between them leaves the
    /// spend history detached from batches that still exist.
    /// </summary>
    public async Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var batches = await _context.ReviewBatches
            .Where(rb => rb.SourceId == sourceId)
            .ToListAsync(cancellationToken);

        if (batches.Count == 0)
        {
            return;
        }

        // The cost ledger outlives the batches it references (its FK is NoAction by
        // design) — detach the link instead of losing the spend history.
        var batchIds = batches.Select(b => b.Id).ToList();
        var usageRecords = await _context.AiUsageRecords
            .Where(u => u.ReviewBatchId != null && batchIds.Contains(u.ReviewBatchId.Value))
            .ToListAsync(cancellationToken);
        foreach (var record in usageRecords)
        {
            record.ReviewBatchId = null;
        }

        _context.ReviewBatches.RemoveRange(batches);
        await _context.SaveChangesAsync(cancellationToken);
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
        var batch = await _context.LoadForUpdateAsync<ReviewBatch>(id, cancellationToken);

        batch.Status = status;

        if (status == ReviewBatchStatus.Completed)
        {
            batch.CompletedAt = DateTimeOffset.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateCompletedAsync(Guid id, DateTimeOffset completedAt, CancellationToken cancellationToken = default)
    {
        var batch = await _context.LoadForUpdateAsync<ReviewBatch>(id, cancellationToken);

        batch.Status = ReviewBatchStatus.Completed;
        batch.CompletedAt = completedAt;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
