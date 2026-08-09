using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCampaignRecapRepository : ICampaignRecapRepository
{
    private readonly List<CampaignRecap> _recaps = [];

    public IReadOnlyList<CampaignRecap> Recaps => _recaps.AsReadOnly();

    public void Seed(params CampaignRecap[] recaps) => _recaps.AddRange(recaps);

    public Task<CampaignRecap?> GetByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_recaps.FirstOrDefault(r => r.CampaignId == campaignId));

    /// <summary>One row per campaign, replaced in place — mirrors the real upsert.</summary>
    public Task UpsertAsync(CampaignRecap recap, CancellationToken cancellationToken = default)
    {
        _recaps.RemoveAll(r => r.CampaignId == recap.CampaignId);
        _recaps.Add(recap);
        return Task.CompletedTask;
    }
}
