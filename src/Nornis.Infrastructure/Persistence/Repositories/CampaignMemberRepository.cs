using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class CampaignMemberRepository : ICampaignMemberRepository
{
    private readonly NornisDbContext _context;

    public CampaignMemberRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<CampaignMember> CreateAsync(CampaignMember member, CancellationToken cancellationToken = default)
    {
        _context.CampaignMembers.Add(member);
        await _context.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<CampaignMember?> GetByCampaignAndUserAsync(Guid campaignId, Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.CampaignMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(cm => cm.CampaignId == campaignId && cm.UserId == userId, cancellationToken);
    }

    public async Task<IReadOnlyList<CampaignMember>> ListByCampaignAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        return await _context.CampaignMembers
            .AsNoTracking()
            .Include(cm => cm.User)
            .Where(cm => cm.CampaignId == campaignId)
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveAsync(CampaignMember member, CancellationToken cancellationToken = default)
    {
        _context.CampaignMembers.Remove(member);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CampaignMember> UpdateAsync(CampaignMember member, CancellationToken cancellationToken = default)
    {
        _context.CampaignMembers.Update(member);
        await _context.SaveChangesAsync(cancellationToken);
        return member;
    }

    public async Task<int> CountByRoleAsync(Guid campaignId, CampaignRole role, CancellationToken cancellationToken = default)
    {
        return await _context.CampaignMembers
            .CountAsync(cm => cm.CampaignId == campaignId && cm.Role == role, cancellationToken);
    }

    public async Task<IReadOnlyList<CampaignMember>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.CampaignMembers
            .AsNoTracking()
            .Where(cm => cm.UserId == userId)
            .ToListAsync(cancellationToken);
    }
}
