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

    /// <summary>
    /// Returns the <see cref="ReviewProposal.ReviewedAt"/> of the campaign's most recently
    /// accepted proposal, or null if the campaign has no accepted proposals. Drives the
    /// continuity-audit auto-trigger (a run is only warranted after new canon was accepted).
    /// </summary>
    Task<DateTimeOffset?> GetLatestAcceptanceTimeAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct campaign ids that have at least one accepted proposal — the only
    /// campaigns the continuity-audit trigger needs to evaluate.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListCampaignIdsWithAcceptancesAsync(
        CancellationToken cancellationToken = default);
}
