using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

/// <summary>
/// Loads a world's rows for export, one untracked query per selected category. The
/// per-category queries mirror <see cref="WorldRepository"/>'s delete graph — anything
/// that dies with a world should be exportable from it.
/// </summary>
public class WorldExportReader : IWorldExportReader
{
    private readonly NornisDbContext _context;

    public WorldExportReader(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<WorldExportData> ReadAsync(
        Guid worldId,
        IReadOnlySet<WorldExportCategory> categories,
        CancellationToken cancellationToken = default)
    {
        async Task<IReadOnlyList<T>> LoadAsync<T>(WorldExportCategory category, IQueryable<T> query) where T : class =>
            categories.Contains(category)
                ? await query.AsNoTracking().ToListAsync(cancellationToken)
                : [];

        return new WorldExportData
        {
            Members = await LoadAsync(WorldExportCategory.Members, _context.WorldMembers
                .Where(m => m.WorldId == worldId).OrderBy(m => m.JoinedAt)),

            Campaigns = await LoadAsync(WorldExportCategory.Campaigns, _context.Campaigns
                .Where(c => c.WorldId == worldId).OrderBy(c => c.CreatedAt)),
            CampaignCharacters = await LoadAsync(WorldExportCategory.Campaigns, _context.CampaignCharacters
                .Where(cc => _context.Campaigns.Any(c => c.Id == cc.CampaignId && c.WorldId == worldId))
                .OrderBy(cc => cc.CreatedAt)),
            StorylineCampaigns = await LoadAsync(WorldExportCategory.Campaigns, _context.StorylineCampaigns
                .Where(sc => _context.Campaigns.Any(c => c.Id == sc.CampaignId && c.WorldId == worldId))
                .OrderBy(sc => sc.CreatedAt)),

            Characters = await LoadAsync(WorldExportCategory.Characters, _context.Characters
                .Where(c => c.WorldId == worldId).OrderBy(c => c.CreatedAt)),

            Sources = await LoadAsync(WorldExportCategory.Sources, _context.Sources
                .Where(s => s.WorldId == worldId).OrderBy(s => s.CreatedAt)),
            SourceExtractions = await LoadAsync(WorldExportCategory.Sources, _context.SourceExtractions
                .Where(e => _context.Sources.Any(s => s.Id == e.SourceId && s.WorldId == worldId))
                .OrderBy(e => e.CreatedAt)),
            SourceReferences = await LoadAsync(WorldExportCategory.Sources, _context.SourceReferences
                .Where(r => _context.Sources.Any(s => s.Id == r.SourceId && s.WorldId == worldId))
                .OrderBy(r => r.CreatedAt)),

            Attachments = await LoadAsync(WorldExportCategory.Attachments, _context.SourceAttachments
                .Where(a => a.WorldId == worldId).OrderBy(a => a.SourceId).ThenBy(a => a.Ord)),

            Artifacts = await LoadAsync(WorldExportCategory.Codex, _context.Artifacts
                .Where(a => a.WorldId == worldId).OrderBy(a => a.CreatedAt)),
            ArtifactFacts = await LoadAsync(WorldExportCategory.Codex, _context.ArtifactFacts
                .Where(f => _context.Artifacts.Any(a => a.Id == f.ArtifactId && a.WorldId == worldId))
                .OrderBy(f => f.CreatedAt)),
            ArtifactRelationships = await LoadAsync(WorldExportCategory.Codex, _context.ArtifactRelationships
                .Where(r => r.WorldId == worldId).OrderBy(r => r.CreatedAt)),

            MapPlacemarks = await LoadAsync(WorldExportCategory.MapPins, _context.MapPlacemarks
                .Where(p => p.WorldId == worldId).OrderBy(p => p.CreatedAt)),

            LibraryDocuments = await LoadAsync(WorldExportCategory.Library, _context.LibraryDocuments
                .Where(d => d.WorldId == worldId).OrderBy(d => d.CreatedAt)),

            ReviewBatches = await LoadAsync(WorldExportCategory.Reviews, _context.ReviewBatches
                .Where(b => b.WorldId == worldId).OrderBy(b => b.CreatedAt)),
            ReviewProposals = await LoadAsync(WorldExportCategory.Reviews, _context.ReviewProposals
                .Where(p => _context.ReviewBatches.Any(b => b.Id == p.ReviewBatchId && b.WorldId == worldId))
                .OrderBy(p => p.CreatedAt)),

            HealthAssessments = await LoadAsync(WorldExportCategory.Health, _context.HealthAssessments
                .Where(h => h.WorldId == worldId).OrderBy(h => h.CreatedAt)),
            ContinuityFindings = await LoadAsync(WorldExportCategory.Health, _context.ContinuityFindings
                .Where(f => _context.HealthAssessments.Any(h => h.Id == f.HealthAssessmentId && h.WorldId == worldId))
                .OrderBy(f => f.Id)),

            AiUsageRecords = await LoadAsync(WorldExportCategory.AiUsage, _context.AiUsageRecords
                .Where(a => a.WorldId == worldId).OrderBy(a => a.CreatedAt)),
        };
    }
}
