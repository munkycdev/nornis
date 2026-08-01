using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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

    public async Task<IReadOnlyList<Artifact>> ListByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];

        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => ids.Contains(a.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        // Tracked-load delete: the InMemory provider used in tests lacks ExecuteDelete.
        // Facts cascade at the database level; the caller guarantees no relationships
        // or character links remain (see IArtifactRepository).
        var artifact = await _context.Artifacts
            .FirstOrDefaultAsync(a => a.Id == artifactId, cancellationToken);

        if (artifact is null)
        {
            return;
        }

        _context.Artifacts.Remove(artifact);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListByWorldAsync(Guid worldId, ArtifactType? type = null, VisibilityScope? visibility = null, CancellationToken cancellationToken = default)
    {
        var query = _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId);

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

    public async Task<IReadOnlyList<Artifact>> ListByEquivalentNameAsync(Guid worldId, string name, VisibilityFilter filter, CancellationToken cancellationToken = default)
    {
        // World, status and visibility stay in SQL — the visibility predicate in particular
        // must not move client-side, since ArtifactNameLookupVisibilityTests exists precisely
        // to stop it drifting from VisibilityFilter.CanSee.
        //
        // The NAME predicate is applied in memory because SQL cannot collapse internal
        // whitespace runs, and ArtifactNameKey is the single policy for what counts as the
        // same name — the apply-time dedup uses it too, and the two must never disagree
        // (a create that dedup-bound to "Salt Factor" while resolution refused to match
        // "Salt  Factor" stranded every fact in the batch that referenced it, permanently).
        // Filtering after the fetch also means ambiguity is counted over the full equivalence
        // set rather than the exact-match subset, so a genuine duplicate is never hidden.
        // A world's artifact count is campaign-scale, and the review queue already loads all
        // of them per page.
        var candidates = await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId
                && a.Status != ArtifactStatus.Archived)
            .Where(filter.CanSeeArtifact())
            .ToListAsync(cancellationToken);

        return candidates
            .Where(a => ArtifactNameKey.AreEquivalent(a.Name, name))
            .ToList();
    }

    public async Task<IReadOnlyList<Artifact>> ListByTypeAsync(
        Guid worldId,
        ArtifactType type,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default)
    {
        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId
                && a.Type == type
                && a.Status != ArtifactStatus.Archived)
            .Where(filter.CanSeeArtifact())
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListRecentByWorldAsync(
        Guid worldId,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId
                && a.Status != ArtifactStatus.Archived)
            .Where(filter.CanSeeArtifact())
            .OrderByDescending(a => a.UpdatedAt)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListByNamesInTextAsync(
        Guid worldId,
        string text,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default)
    {
        var candidates = await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId
                && a.Status != ArtifactStatus.Archived)
            .Where(filter.CanSeeArtifact())
            .ToListAsync(cancellationToken);

        return candidates
            .Where(a => text.Contains(a.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
