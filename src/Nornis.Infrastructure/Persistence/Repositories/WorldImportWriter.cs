using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

/// <summary>
/// Materializes an imported world in a single transaction. Rows arrive with fresh ids and
/// FK columns already remapped; only unattached entities are added, so EF inserts exactly
/// what it is given. The in-memory provider used by API integration tests has no
/// transactions — there a single SaveChanges provides equivalent all-or-nothing semantics.
/// </summary>
public class WorldImportWriter : IWorldImportWriter
{
    private readonly NornisDbContext _context;

    public WorldImportWriter(NornisDbContext context)
    {
        _context = context;
    }

    public async Task WriteAsync(World world, WorldMember member, WorldExportData rows, CancellationToken ct = default)
    {
        _context.Worlds.Add(world);
        _context.WorldMembers.Add(member);
        _context.Campaigns.AddRange(rows.Campaigns);
        _context.Characters.AddRange(rows.Characters);
        _context.CampaignCharacters.AddRange(rows.CampaignCharacters);
        _context.StorylineCampaigns.AddRange(rows.StorylineCampaigns);
        _context.Sources.AddRange(rows.Sources);
        _context.SourceAttachments.AddRange(rows.Attachments);
        _context.SourceExtractions.AddRange(rows.SourceExtractions);
        _context.SourceReferences.AddRange(rows.SourceReferences);
        _context.Artifacts.AddRange(rows.Artifacts);
        _context.ArtifactFacts.AddRange(rows.ArtifactFacts);
        _context.ArtifactRelationships.AddRange(rows.ArtifactRelationships);
        _context.MapPlacemarks.AddRange(rows.MapPlacemarks);

        if (_context.Database.IsRelational())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        else
        {
            await _context.SaveChangesAsync(ct);
        }
    }
}
