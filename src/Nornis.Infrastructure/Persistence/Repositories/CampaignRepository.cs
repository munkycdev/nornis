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

    public async Task<Campaign?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Campaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        _context.Campaigns.Update(campaign);
        await _context.SaveChangesAsync(cancellationToken);
        return campaign;
    }

    public async Task<IReadOnlyList<Campaign>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Campaigns
            .AsNoTracking()
            .Where(c => _context.CampaignMembers.Any(cm => cm.CampaignId == c.Id && cm.UserId == userId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Campaign>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await _context.Campaigns
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
    }
}
