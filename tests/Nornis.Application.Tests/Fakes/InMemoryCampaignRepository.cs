using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryCampaignRepository : ICampaignRepository
{
    private readonly List<Campaign> _campaigns = [];
    private readonly InMemorySourceRepository? _sourceRepository;
    private readonly InMemoryCharacterRepository? _characterRepository;

    public IReadOnlyList<Campaign> Campaigns => _campaigns.AsReadOnly();

    public InMemoryCampaignRepository()
    {
    }

    /// <summary>
    /// Creates a repository whose DeleteAsync mirrors the real one: sources are
    /// detached (CampaignId nulled) and campaign-character assignments removed.
    /// </summary>
    public InMemoryCampaignRepository(
        InMemorySourceRepository? sourceRepository = null,
        InMemoryCharacterRepository? characterRepository = null)
    {
        _sourceRepository = sourceRepository;
        _characterRepository = characterRepository;
    }

    public void Seed(params Campaign[] campaigns) => _campaigns.AddRange(campaigns);

    public Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        _campaigns.Add(campaign);
        return Task.FromResult(campaign);
    }

    public Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_campaigns.FirstOrDefault(c => c.Id == campaignId));
    }

    public Task<IReadOnlyList<Campaign>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var campaigns = _campaigns
            .Where(c => c.WorldId == worldId)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<Campaign>>(campaigns.AsReadOnly());
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

    public Task DeleteAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        if (_sourceRepository is not null)
        {
            foreach (var source in _sourceRepository.Sources.Where(s => s.CampaignId == campaignId))
            {
                source.CampaignId = null;
            }
        }

        _characterRepository?.RemoveAssignmentsByCampaign(campaignId);

        _campaigns.RemoveAll(c => c.Id == campaignId);
        return Task.CompletedTask;
    }
}
