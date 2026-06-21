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

    public async Task<IReadOnlyList<ReviewBatch>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewBatches
            .AsNoTracking()
            .Where(rb => rb.CampaignId == campaignId)
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
}
