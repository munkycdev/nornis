using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ReviewProposalRepository : IReviewProposalRepository
{
    private readonly NornisDbContext _context;

    public ReviewProposalRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ReviewProposal> CreateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default)
    {
        _context.ReviewProposals.Add(proposal);
        await _context.SaveChangesAsync(cancellationToken);
        return proposal;
    }

    public async Task<ReviewProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(rp => rp.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewProposal>> ListByReviewBatchAsync(Guid reviewBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => rp.ReviewBatchId == reviewBatchId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewProposal>> ListPendingByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => _context.ReviewBatches.Any(rb => rb.Id == rp.ReviewBatchId && rb.CampaignId == campaignId))
            .Where(rp => rp.Status == ReviewProposalStatus.Pending)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReviewProposal> UpdateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default)
    {
        _context.ReviewProposals.Update(proposal);
        await _context.SaveChangesAsync(cancellationToken);
        return proposal;
    }
}
