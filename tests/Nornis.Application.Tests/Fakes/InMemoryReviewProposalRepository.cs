using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryReviewProposalRepository : IReviewProposalRepository
{
    private readonly List<ReviewProposal> _proposals = [];
    private readonly InMemoryReviewBatchRepository? _batchRepository;

    public IReadOnlyList<ReviewProposal> Proposals => _proposals.AsReadOnly();

    public InMemoryReviewProposalRepository()
    {
    }

    public InMemoryReviewProposalRepository(InMemoryReviewBatchRepository batchRepository)
    {
        _batchRepository = batchRepository;
    }

    public Task<ReviewProposal> CreateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default)
    {
        _proposals.Add(proposal);
        return Task.FromResult(proposal);
    }

    public Task<ReviewProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var proposal = _proposals.FirstOrDefault(p => p.Id == id);
        return Task.FromResult(proposal);
    }

    public Task<IReadOnlyList<ReviewProposal>> ListByReviewBatchAsync(Guid reviewBatchId, CancellationToken cancellationToken = default)
    {
        var proposals = _proposals.Where(p => p.ReviewBatchId == reviewBatchId).ToList();
        return Task.FromResult<IReadOnlyList<ReviewProposal>>(proposals.AsReadOnly());
    }

    public Task<IReadOnlyList<ReviewProposal>> ListPendingByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        // For this fake, we don't have campaign info directly on proposals,
        // so we return all pending proposals. Tests can filter as needed.
        var proposals = _proposals
            .Where(p => p.Status == ReviewProposalStatus.Pending)
            .ToList();
        return Task.FromResult<IReadOnlyList<ReviewProposal>>(proposals.AsReadOnly());
    }

    public Task<ReviewProposal> UpdateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default)
    {
        var index = _proposals.FindIndex(p => p.Id == proposal.Id);
        if (index >= 0)
        {
            _proposals[index] = proposal;
        }
        return Task.FromResult(proposal);
    }

    public Task<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)> ListReviewQueueAsync(
        Guid campaignId,
        IReadOnlyList<Guid> allowedSourceIds,
        Guid? filterByBatchId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var batches = _batchRepository?.Batches ?? [];

        var query = _proposals
            .Where(p => p.Status == ReviewProposalStatus.Pending)
            .Join(
                batches.Where(b => b.CampaignId == campaignId && allowedSourceIds.Contains(b.SourceId)),
                p => p.ReviewBatchId,
                b => b.Id,
                (p, b) => new { Proposal = p, Batch = b });

        if (filterByBatchId.HasValue)
        {
            query = query.Where(x => x.Proposal.ReviewBatchId == filterByBatchId.Value);
        }

        var results = query
            .OrderBy(x => x.Batch.CreatedAt)
            .ThenBy(x => x.Proposal.CreatedAt)
            .Select(x => x.Proposal)
            .Take(limit + 1)
            .ToList();

        var hasMore = results.Count > limit;
        var proposals = hasMore ? results.Take(limit).ToList() : results;

        return Task.FromResult<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)>(
            (proposals.AsReadOnly(), hasMore));
    }
}
