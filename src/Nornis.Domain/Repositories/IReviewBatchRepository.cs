using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IReviewBatchRepository
{
    Task<ReviewBatch> CreateAsync(ReviewBatch batch, CancellationToken cancellationToken = default);

    Task<ReviewBatch?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewBatch>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(Guid id, ReviewBatchStatus status, CancellationToken cancellationToken = default);
}
