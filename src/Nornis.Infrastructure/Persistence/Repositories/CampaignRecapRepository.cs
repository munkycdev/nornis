using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class CampaignRecapRepository : ICampaignRecapRepository
{
    private readonly NornisDbContext _context;

    public CampaignRecapRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<CampaignRecap?> GetByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return await _context.CampaignRecaps
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CampaignId == campaignId, cancellationToken);
    }

    public async Task UpsertAsync(CampaignRecap recap, CancellationToken cancellationToken = default)
    {
        var existing = await _context.CampaignRecaps
            .FirstOrDefaultAsync(r => r.CampaignId == recap.CampaignId, cancellationToken);

        if (existing is null)
        {
            _context.CampaignRecaps.Add(recap);
        }
        else
        {
            existing.GmContentMarkdown = recap.GmContentMarkdown;
            existing.PartyContentMarkdown = recap.PartyContentMarkdown;
            existing.Model = recap.Model;
            existing.GeneratedAt = recap.GeneratedAt;
            existing.GeneratedByUserId = recap.GeneratedByUserId;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two generations raced on the insert; the unique index let one through. The
            // loser's recap is a regenerable read-model built seconds apart from the
            // winner's — losing it quietly is correct (same shape as WorldDigest).
            _context.ChangeTracker.Clear();
        }
    }
}
