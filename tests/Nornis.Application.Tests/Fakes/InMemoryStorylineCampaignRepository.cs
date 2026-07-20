using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryStorylineCampaignRepository : IStorylineCampaignRepository
{
    private readonly List<StorylineCampaign> _links = [];

    public IReadOnlyList<StorylineCampaign> Links => _links.AsReadOnly();

    public void Seed(Guid artifactId, params Guid[] campaignIds)
    {
        foreach (var campaignId in campaignIds)
        {
            _links.Add(new StorylineCampaign
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifactId,
                CampaignId = campaignId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    public Task<IReadOnlyList<StorylineCampaign>> ListByArtifactIdsAsync(
        IReadOnlyCollection<Guid> artifactIds, CancellationToken cancellationToken = default)
    {
        var ids = artifactIds.ToHashSet();
        return Task.FromResult<IReadOnlyList<StorylineCampaign>>(
            _links.Where(l => ids.Contains(l.ArtifactId)).ToList());
    }

    public Task<IReadOnlyList<StorylineCampaign>> ListByArtifactIdAsync(
        Guid artifactId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<StorylineCampaign>>(
            _links.Where(l => l.ArtifactId == artifactId).ToList());
    }

    public Task ReplaceForStorylineAsync(
        Guid artifactId, IReadOnlyCollection<Guid> campaignIds, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        var desired = campaignIds.ToHashSet();
        _links.RemoveAll(l => l.ArtifactId == artifactId && !desired.Contains(l.CampaignId));

        var current = _links.Where(l => l.ArtifactId == artifactId).Select(l => l.CampaignId).ToHashSet();
        foreach (var campaignId in desired.Except(current))
        {
            _links.Add(new StorylineCampaign
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifactId,
                CampaignId = campaignId,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = actingUserId
            });
        }

        return Task.CompletedTask;
    }
}
