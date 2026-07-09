using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class CampaignRepository : ICampaignRepository
{
    private readonly NornisDbContext _context;

    public CampaignRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Campaign> CreateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync(cancellationToken);
        return campaign;
    }

    public async Task<Campaign?> GetByIdAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return await _context.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken);
    }

    public async Task<IReadOnlyList<Campaign>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.Campaigns
            .AsNoTracking()
            .Where(c => c.WorldId == worldId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        _context.Campaigns.Update(campaign);
        await _context.SaveChangesAsync(cancellationToken);
        return campaign;
    }

    public async Task DeleteAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        // The database intentionally does not cascade these (multiple-cascade-path
        // restriction); detach dependents first so knowledge and sources survive.
        await _context.Sources
            .Where(s => s.CampaignId == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.CampaignId, (Guid?)null), cancellationToken);

        await _context.CampaignCharacters
            .Where(cc => cc.CampaignId == campaignId)
            .ExecuteDeleteAsync(cancellationToken);

        await _context.Campaigns
            .Where(c => c.Id == campaignId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
