using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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
        // Last in the world's order. A world still at all-zeroes gets its first 1 here, which
        // pushes the newcomer behind the zeroes — the same "newest last" the GM just asked for
        // by creating it, and the rest keep their newest-first fallback until a real reorder.
        var maxSortOrder = await _context.Campaigns
            .Where(c => c.WorldId == campaign.WorldId)
            .Select(c => (int?)c.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;

        campaign.SortOrder = maxSortOrder + 1;

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
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CampaignRollup> GetRollupAsync(
        Guid worldId,
        Guid campaignId,
        VisibilityFilter filter,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var ranked = RankedRollupQuery(worldId, campaignId, filter);

        // Two reads off one query definition: the page, and the height of what it was cut
        // from. Building the shape once is what keeps the count honest about the same set.
        var total = await ranked.CountAsync(cancellationToken);

        var artifacts = await ranked
            .Take(limit)
            .ToListAsync(cancellationToken);

        return new CampaignRollup(artifacts, total);
    }

    /// <summary>
    /// Artifacts evidenced by the campaign's visible sources, ranked by how many of them
    /// cite each. Citations reach an artifact three ways — directly, through one of its
    /// facts, or through a relationship it is an end of — so the arms below are unioned
    /// before grouping; an artifact cited twice by the same source through two routes must
    /// still count that source once.
    /// </summary>
    private IQueryable<CampaignRollupArtifact> RankedRollupQuery(
        Guid worldId, Guid campaignId, VisibilityFilter filter)
    {
        var sourceIds = _context.Sources
            .Where(s => s.WorldId == worldId && s.CampaignId == campaignId)
            .Where(filter.CanSeeSource())
            .Select(s => s.Id);

        var references = _context.SourceReferences
            .Where(r => sourceIds.Contains(r.SourceId));

        var direct = references
            .Where(r => r.TargetType == SourceReferenceTargetType.Artifact)
            .Select(r => new { r.SourceId, ArtifactId = r.TargetId });

        var viaFacts =
            from r in references.Where(r => r.TargetType == SourceReferenceTargetType.ArtifactFact)
            join f in _context.ArtifactFacts.Where(filter.CanSeeFact()) on r.TargetId equals f.Id
            select new { r.SourceId, ArtifactId = f.ArtifactId };

        var relationshipRefs = references
            .Where(r => r.TargetType == SourceReferenceTargetType.ArtifactRelationship);
        var visibleRelationships = _context.ArtifactRelationships.Where(filter.CanSeeRelationship());

        var viaRelationshipA =
            from r in relationshipRefs
            join rel in visibleRelationships on r.TargetId equals rel.Id
            select new { r.SourceId, ArtifactId = rel.ArtifactAId };

        var viaRelationshipB =
            from r in relationshipRefs
            join rel in visibleRelationships on r.TargetId equals rel.Id
            select new { r.SourceId, ArtifactId = rel.ArtifactBId };

        var citations = direct
            .Concat(viaFacts)
            .Concat(viaRelationshipA)
            .Concat(viaRelationshipB);

        return from c in citations
               join a in _context.Artifacts
                       .Where(a => a.WorldId == worldId)
                       .Where(filter.CanSeeArtifact())
                   on c.ArtifactId equals a.Id
               group c.SourceId by new { a.Id, a.Name, a.Type, a.Summary, a.Status, a.Slug }
               into g
               orderby g.Select(sourceId => sourceId).Distinct().Count() descending, g.Key.Name
               select new CampaignRollupArtifact(
                   g.Key.Id,
                   g.Key.Name,
                   g.Key.Type,
                   g.Key.Summary,
                   g.Key.Status,
                   g.Select(sourceId => sourceId).Distinct().Count(),
                   g.Key.Slug);
    }

    public async Task<IReadOnlyList<Campaign>> ReorderAsync(
        Guid worldId, IReadOnlyList<Guid> orderedCampaignIds, CancellationToken cancellationToken = default)
    {
        var campaigns = await _context.Campaigns
            .Where(c => c.WorldId == worldId)
            .ToListAsync(cancellationToken);

        // Ids the caller named, in the order given, then anything it left out — a campaign
        // created between the client's read and this write must still land somewhere, and
        // trailing it is the only placement that cannot displace a position the GM chose.
        var position = 1;
        foreach (var id in orderedCampaignIds)
        {
            if (campaigns.FirstOrDefault(c => c.Id == id) is { } named)
            {
                named.SortOrder = position++;
            }
        }

        var namedIds = orderedCampaignIds.ToHashSet();
        foreach (var missed in campaigns.Where(c => !namedIds.Contains(c.Id)).OrderByDescending(c => c.CreatedAt))
        {
            missed.SortOrder = position++;
        }

        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();

        return campaigns.OrderBy(c => c.SortOrder).ToList();
    }

    public async Task<Campaign> UpdateAsync(Campaign campaign, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(campaign, cancellationToken);
        return campaign;
    }

    public async Task DeleteAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        // The database intentionally does not cascade these (multiple-cascade-path
        // restriction); detach dependents first so knowledge and sources survive.
        await _context.SetWhereAsync<Source, Guid?>(
            s => s.CampaignId == campaignId, s => s.CampaignId, null, cancellationToken);

        // A world pointing at this campaign as its current one is a dependent like any other,
        // and here for the same reason the others are: the FK is Restrict, so the delete below
        // would fail outright rather than quietly leaving a dangling pointer.
        await _context.SetWhereAsync<World, Guid?>(
            w => w.CurrentCampaignId == campaignId, w => w.CurrentCampaignId, null, cancellationToken);

        await _context.DeleteWhereAsync<CampaignCharacter>(
            cc => cc.CampaignId == campaignId, cancellationToken);

        await _context.DeleteWhereAsync<CampaignRecap>(
            r => r.CampaignId == campaignId, cancellationToken);

        await _context.DeleteWhereAsync<Campaign>(c => c.Id == campaignId, cancellationToken);
    }
}
