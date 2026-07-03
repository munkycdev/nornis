using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCampaignRepository : ICampaignRepository
{
    private readonly List<Campaign> _campaigns = [];
    private readonly InMemoryCampaignMemberRepository? _memberRepository;

    public IReadOnlyList<Campaign> Campaigns => _campaigns.AsReadOnly();

    public InMemoryCampaignRepository()
    {
    }

    /// <summary>
    /// Creates an InMemoryCampaignRepository that uses membership records to filter ListByUserAsync,
    /// matching the real EF Core repository behavior.
    /// </summary>
    public InMemoryCampaignRepository(InMemoryCampaignMemberRepository memberRepository)
    {
        _memberRepository = memberRepository;
    }

    public Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        _campaigns.Add(campaign);
        return Task.FromResult(campaign);
    }

    public Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var campaign = _campaigns.FirstOrDefault(c => c.Id == id);
        return Task.FromResult(campaign);
    }

    public Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        var index = _campaigns.FindIndex(c => c.Id == campaign.Id);
        if (index >= 0)
        {
            _campaigns[index] = campaign;
        }
        return Task.FromResult(campaign);
    }

    public Task<IReadOnlyList<Campaign>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        IEnumerable<Campaign> campaigns;

        if (_memberRepository is not null)
        {
            // Match real repository behavior: filter by membership
            var memberCampaignIds = _memberRepository.Members
                .Where(m => m.UserId == userId)
                .Select(m => m.CampaignId)
                .ToHashSet();
            campaigns = _campaigns.Where(c => memberCampaignIds.Contains(c.Id));
        }
        else
        {
            // Fallback for backward compatibility: filter by CreatedByUserId
            campaigns = _campaigns.Where(c => c.CreatedByUserId == userId);
        }

        return Task.FromResult<IReadOnlyList<Campaign>>(campaigns.ToList().AsReadOnly());
    }

    public Task<IReadOnlyList<Campaign>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        var campaigns = _campaigns.Where(c => ids.Contains(c.Id)).ToList();
        return Task.FromResult<IReadOnlyList<Campaign>>(campaigns.AsReadOnly());
    }
}
