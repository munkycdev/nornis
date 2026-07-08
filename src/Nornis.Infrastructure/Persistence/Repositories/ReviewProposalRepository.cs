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

    public async Task<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)> ListReviewQueueAsync(
        Guid campaignId,
        IReadOnlyList<Guid> allowedSourceIds,
        Guid? filterByBatchId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReviewProposals
            .AsNoTracking()
            .Join(
                _context.ReviewBatches,
                rp => rp.ReviewBatchId,
                rb => rb.Id,
                (rp, rb) => new { Proposal = rp, Batch = rb })
            .Where(x => x.Batch.CampaignId == campaignId)
            .Where(x => allowedSourceIds.Contains(x.Batch.SourceId))
            .Where(x => x.Proposal.Status == ReviewProposalStatus.Pending);

        if (filterByBatchId.HasValue)
        {
            query = query.Where(x => x.Proposal.ReviewBatchId == filterByBatchId.Value);
        }

        var results = await query
            .OrderBy(x => x.Batch.CreatedAt)
            .ThenBy(x => x.Proposal.CreatedAt)
            .Select(x => x.Proposal)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = results.Count > limit;
        var proposals = hasMore ? results.Take(limit).ToList() : results;

        return (proposals.AsReadOnly(), hasMore);
    }

    public async Task<DateTimeOffset?> GetLatestAcceptanceTimeAsync(
        Guid campaignId, CancellationToken cancellationToken = default)
    {
        var latest = await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => rp.Status == ReviewProposalStatus.Accepted && rp.ReviewedAt != null)
            .Where(rp => _context.ReviewBatches.Any(rb => rb.Id == rp.ReviewBatchId && rb.CampaignId == campaignId))
            .OrderByDescending(rp => rp.ReviewedAt)
            .Select(rp => rp.ReviewedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest;
    }

    public async Task<IReadOnlyList<Guid>> ListCampaignIdsWithAcceptancesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => rp.Status == ReviewProposalStatus.Accepted)
            .Join(
                _context.ReviewBatches,
                rp => rp.ReviewBatchId,
                rb => rb.Id,
                (rp, rb) => rb.CampaignId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
