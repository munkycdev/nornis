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

    public async Task<IReadOnlyList<Artifact>> SearchByNameAsync(Guid worldId, string searchTerm, CancellationToken cancellationToken = default)
    {
        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId && EF.Functions.Like(a.Name, $"%{searchTerm}%"))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListByExactNameAsync(Guid worldId, string name, CancellationToken cancellationToken = default)
    {
        // Default SQL Server collation is case-insensitive; ToLower makes the intent
        // explicit and keeps the in-memory test provider behaviour identical.
        var normalized = name.ToLowerInvariant();

        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId && a.Name.ToLower() == normalized)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Artifact>> ListRecentByWorldAsync(
        Guid worldId,
        VisibilityFilter filter,
        int maxCount,
        CancellationToken cancellationToken = default)
    {
        // Hoisted locals translate to SQL parameters.
        var scopes = filter.Scopes;
        var owner = filter.PrivateOwnerUserId;

        return await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId
                && a.Status != ArtifactStatus.Archived
                && scopes.Contains(a.Visibility)
                && (a.Visibility != VisibilityScope.Private || owner == null || a.CreatedByUserId == owner))
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
        var scopes = filter.Scopes;
        var owner = filter.PrivateOwnerUserId;

        var candidates = await _context.Artifacts
            .AsNoTracking()
            .Where(a => a.WorldId == worldId
                && a.Status != ArtifactStatus.Archived
                && scopes.Contains(a.Visibility)
                && (a.Visibility != VisibilityScope.Private || owner == null || a.CreatedByUserId == owner))
            .ToListAsync(cancellationToken);

        return candidates
            .Where(a => text.Contains(a.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
