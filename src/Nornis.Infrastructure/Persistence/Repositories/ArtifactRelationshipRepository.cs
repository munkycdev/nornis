using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ArtifactRelationshipRepository : IArtifactRelationshipRepository
{
    private readonly NornisDbContext _context;

    public ArtifactRelationshipRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ArtifactRelationship> CreateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default)
    {
        _context.ArtifactRelationships.Add(relationship);
        await _context.SaveChangesAsync(cancellationToken);
        return relationship;
    }

    public async Task<ArtifactRelationship?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ArtifactRelationships
            .AsNoTracking()
            .FirstOrDefaultAsync(ar => ar.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        return await _context.ArtifactRelationships
            .AsNoTracking()
            .Where(ar => ar.ArtifactAId == artifactId || ar.ArtifactBId == artifactId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactRelationship>> ListByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
            return [];

        return await _context.ArtifactRelationships
            .AsNoTracking()
            .Where(ar => ids.Contains(ar.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactRelationship>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        VisibilityFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (artifactIds.Count == 0)
            return [];

        // Hoisted locals translate to SQL parameters.
        var scopes = filter.Scopes;
        var owner = filter.PrivateOwnerUserId;

        return await _context.ArtifactRelationships
            .AsNoTracking()
            .Where(ar =>
                (artifactIds.Contains(ar.ArtifactAId) || artifactIds.Contains(ar.ArtifactBId))
                && scopes.Contains(ar.Visibility)
                && (ar.Visibility != VisibilityScope.Private || owner == null || ar.CreatedByUserId == owner))
            .ToListAsync(cancellationToken);
    }

    public async Task<ArtifactRelationship> UpdateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default)
    {
        _context.ArtifactRelationships.Update(relationship);
        await _context.SaveChangesAsync(cancellationToken);
        return relationship;
    }

    public async Task DeleteAsync(Guid relationshipId, CancellationToken cancellationToken = default)
    {
        var relationship = await _context.ArtifactRelationships
            .FirstOrDefaultAsync(ar => ar.Id == relationshipId, cancellationToken);
        if (relationship is not null)
        {
            _context.ArtifactRelationships.Remove(relationship);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
