using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ICampaignRepository
{
    Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default);

    Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Campaign>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the campaign after detaching dependents that the database does not
    /// cascade: clears Source.CampaignId and removes campaign-character assignments.
    /// Knowledge and sources are never deleted with a campaign.
    /// </summary>
    Task DeleteAsync(Guid campaignId, CancellationToken cancellationToken = default);
}
