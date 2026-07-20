using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class StorylineCampaignRepository : IStorylineCampaignRepository
{
    private readonly NornisDbContext _context;

    public StorylineCampaignRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<StorylineCampaign>> ListByArtifactIdsAsync(
        IReadOnlyCollection<Guid> artifactIds, CancellationToken cancellationToken = default)
    {
        if (artifactIds.Count == 0)
        {
            return [];
        }

        var ids = artifactIds.ToList();
        return await _context.StorylineCampaigns
            .AsNoTracking()
            .Where(sc => ids.Contains(sc.ArtifactId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StorylineCampaign>> ListByArtifactIdAsync(
        Guid artifactId, CancellationToken cancellationToken = default)
    {
        return await _context.StorylineCampaigns
            .AsNoTracking()
            .Where(sc => sc.ArtifactId == artifactId)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceForStorylineAsync(
        Guid artifactId, IReadOnlyCollection<Guid> campaignIds, Guid? actingUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.StorylineCampaigns
            .Where(sc => sc.ArtifactId == artifactId)
            .ToListAsync(cancellationToken);

        var desired = campaignIds.ToHashSet();
        var current = existing.Select(sc => sc.CampaignId).ToHashSet();

        _context.StorylineCampaigns.RemoveRange(existing.Where(sc => !desired.Contains(sc.CampaignId)));

        var now = DateTimeOffset.UtcNow;
        foreach (var campaignId in desired.Except(current))
        {
            _context.StorylineCampaigns.Add(new StorylineCampaign
            {
                Id = Guid.NewGuid(),
                ArtifactId = artifactId,
                CampaignId = campaignId,
                CreatedAt = now,
                CreatedByUserId = actingUserId
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
