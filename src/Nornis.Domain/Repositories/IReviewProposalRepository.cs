using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IReviewProposalRepository
{
    Task<ReviewProposal> CreateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default);

    Task<ReviewProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewProposal>> ListByReviewBatchAsync(Guid reviewBatchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewProposal>> ListPendingByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task<ReviewProposal> UpdateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)> ListReviewQueueAsync(
        Guid campaignId,
        IReadOnlyList<Guid> allowedSourceIds,
        Guid? filterByBatchId,
        int limit,
        CancellationToken cancellationToken = default);
}
