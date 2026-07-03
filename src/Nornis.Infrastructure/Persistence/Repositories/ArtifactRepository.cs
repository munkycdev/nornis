using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ArtifactRepository : IArtifactRepository
{
    private readonly NornisDbContext _context;

    public ArtifactRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<Artifact> CreateAsync(Artifact artifact, CancellationToken cancellationToken = default)
    {
        _context.Artifacts.Add(artifact);
        await _context.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    public async Task<Artifact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Artifacts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListByCampaignAsync(Guid campaignId, ArtifactType? type = null, VisibilityScope? visibility = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Artifacts
            .AsNoTracking()
            .Where(a => a.CampaignId == campaignId);

        if (type is not null)
        {
            query = query.Where(a => a.Type == type.Value);
        }

        if (visibility is not null)
        {
            query = query.Where(a => a.Visibility == visibility.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<Artifact> UpdateAsync(Artifact artifact, CancellationToken cancellationToken = default)
    {
        _context.Artifacts.Update(artifact);
        await _context.SaveChangesAsync(cancellationToken);
        return artifact;
    }

    public async Task<IReadOnlyList<Artifact>> SearchByNameAsync(Guid campaignId, string searchTerm, CancellationToken cancellationToken = default)
    {
        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.CampaignId == campaignId && EF.Functions.Like(a.Name, $"%{searchTerm}%"))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListRecentByCampaignAsync(
        Guid campaignId,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.CampaignId == campaignId && allowedVisibilities.Contains(a.Visibility))
            .OrderByDescending(a => a.UpdatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(
        Guid campaignId,
        string text,
        IReadOnlyList<VisibilityScope> allowedVisibilities,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.CampaignId == campaignId && allowedVisibilities.Contains(a.Visibility))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(a => text.Contains(a.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
