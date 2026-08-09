using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ICampaignRecapRepository
{
    Task<CampaignRecap?> GetByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the campaign's recap — the row is the record, one per campaign. Two GMs
    /// generating at once is a last-write-wins race on a regenerable read-model; the
    /// loser's spend is wasted, nothing is corrupted.
    /// </summary>
    Task UpsertAsync(CampaignRecap recap, CancellationToken cancellationToken = default);
}
