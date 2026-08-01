using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorldRepository : IWorldRepository
{
    private readonly NornisDbContext _context;

    public WorldRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<World> CreateAsync(World world, CancellationToken cancellationToken = default)
    {
        _context.Worlds.Add(world);
        await _context.SaveChangesAsync(cancellationToken);
        return world;
    }

    public async Task<World?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Worlds
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<World?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalized = slug.ToLowerInvariant();
        return await _context.Worlds
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.PublicSlug == normalized, cancellationToken);
    }

    public async Task<World> UpdateAsync(World world, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(world, cancellationToken);
        return world;
    }

    public async Task<IReadOnlyList<World>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Ordered by name so the world switcher is stable across requests; without it the
        // list order is whatever SQL Server happens to return.
        return await _context.Worlds
            .AsNoTracking()
            .Where(c => _context.WorldMembers.Any(cm => cm.WorldId == c.Id && cm.UserId == userId))
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<World>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        return await _context.Worlds
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountDemoWorldsCreatedSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return await _context.Worlds
            .AsNoTracking()
            .CountAsync(c => c.IsDemo && c.CreatedByUserId == userId && c.CreatedAt >= since, cancellationToken);
    }

    public async Task<bool> TryClaimContinuityAuditAsync(
        Guid worldId,
        DateTimeOffset claimedAt,
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken = default)
    {
        // The predicate is the lock. A single UPDATE ... WHERE decides the winner inside the
        // database, so two hosts racing on the same world produce one row affected and one zero
        // — no read-then-write window for both to pass through.
        if (_context.Database.IsRelational())
        {
            var affected = await _context.Worlds
                .Where(w => w.Id == worldId
                            && (w.ContinuityAuditClaimedAt == null || w.ContinuityAuditClaimedAt <= staleBefore))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(w => w.ContinuityAuditClaimedAt, claimedAt),
                    cancellationToken);

            return affected == 1;
        }

        // InMemory (API integration tests) has no ExecuteUpdate. Single-threaded there, so a
        // read-modify-write reproduces the observable contract without the atomicity.
        var world = await _context.Worlds.FirstOrDefaultAsync(w => w.Id == worldId, cancellationToken);
        if (world is null || (world.ContinuityAuditClaimedAt is { } existing && existing > staleBefore))
        {
            return false;
        }

        world.ContinuityAuditClaimedAt = claimedAt;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task DeleteAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        // ExecuteDelete needs a relational provider; the API integration tests run on
        // InMemory, which gets tracked RemoveRange + a single SaveChanges instead.
        if (_context.Database.IsRelational())
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            await DeleteWorldGraphAsync(worldId, relational: true, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await DeleteWorldGraphAsync(worldId, relational: false, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Deletes every row belonging to a world, children before parents. Several FKs are
    /// deliberately Restrict/NoAction (SQL Server's multiple-cascade-path restriction), so
    /// the order below matters; nothing here relies on database cascades.
    /// </summary>
    private async Task DeleteWorldGraphAsync(Guid worldId, bool relational, CancellationToken ct)
    {
        async Task DeleteAsync<T>(IQueryable<T> query) where T : class
        {
            if (relational)
            {
                await query.ExecuteDeleteAsync(ct);
            }
            else
            {
                _context.RemoveRange(await query.ToListAsync(ct));
            }
        }

        // The usage ledger's Source/ReviewBatch FKs are NoAction, so it goes first. A world
        // wipe removes its spend history too — this is the one place that ledger rows die.
        await DeleteAsync(_context.AiUsageRecords.Where(a => a.WorldId == worldId));

        // Review pipeline (proposals hang off batches; batches Restrict their source).
        await DeleteAsync(_context.ReviewProposals
            .Where(p => _context.ReviewBatches.Any(b => b.Id == p.ReviewBatchId && b.WorldId == worldId)));
        await DeleteAsync(_context.ReviewBatches.Where(b => b.WorldId == worldId));

        // Source satellites, then the map pins that sit on source attachments.
        await DeleteAsync(_context.MapPlacemarks.Where(p => p.WorldId == worldId));
        await DeleteAsync(_context.SourceReferences
            .Where(r => _context.Sources.Any(s => s.Id == r.SourceId && s.WorldId == worldId)));
        await DeleteAsync(_context.SourceExtractions
            .Where(e => _context.Sources.Any(s => s.Id == e.SourceId && s.WorldId == worldId)));
        await DeleteAsync(_context.SourceAttachments.Where(a => a.WorldId == worldId));

        // Continuity health.
        await DeleteAsync(_context.ContinuityFindings
            .Where(f => _context.HealthAssessments.Any(h => h.Id == f.HealthAssessmentId && h.WorldId == worldId)));
        await DeleteAsync(_context.HealthAssessments.Where(h => h.WorldId == worldId));
        await DeleteAsync(_context.ContinuityDismissals.Where(d => d.WorldId == worldId));

        // Library.
        await DeleteAsync(_context.LibraryChunks.Where(c => c.WorldId == worldId));
        await DeleteAsync(_context.LibraryDocuments.Where(d => d.WorldId == worldId));

        // Knowledge graph. Relationships Restrict their artifacts; characters hold a
        // NoAction FK to their artifact, so they go before Artifacts.
        await DeleteAsync(_context.ArtifactFacts
            .Where(f => _context.Artifacts.Any(a => a.Id == f.ArtifactId && a.WorldId == worldId)));
        await DeleteAsync(_context.ArtifactRelationships.Where(r => r.WorldId == worldId));
        await DeleteAsync(_context.CampaignCharacters
            .Where(cc => _context.Campaigns.Any(c => c.Id == cc.CampaignId && c.WorldId == worldId)));
        await DeleteAsync(_context.StorylineCampaigns
            .Where(sc => _context.Campaigns.Any(c => c.Id == sc.CampaignId && c.WorldId == worldId)));
        await DeleteAsync(_context.Characters.Where(c => c.WorldId == worldId));
        await DeleteAsync(_context.Artifacts.Where(a => a.WorldId == worldId));

        // Sources before Campaigns (Source.CampaignId is Restrict), then the shell.
        await DeleteAsync(_context.ExtractionReplays.Where(r => r.WorldId == worldId));
        await DeleteAsync(_context.Sources.Where(s => s.WorldId == worldId));
        await DeleteAsync(_context.Campaigns.Where(c => c.WorldId == worldId));
        await DeleteAsync(_context.WorldInvites.Where(i => i.WorldId == worldId));
        await DeleteAsync(_context.WorldMembers.Where(m => m.WorldId == worldId));
        await DeleteAsync(_context.Worlds.Where(w => w.Id == worldId));
    }
}
