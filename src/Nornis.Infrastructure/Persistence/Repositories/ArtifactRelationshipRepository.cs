using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
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

    public async Task<ArtifactRelationship> UpdateAsync(ArtifactRelationship relationship, CancellationToken cancellationToken = default)
    {
        _context.ArtifactRelationships.Update(relationship);
        await _context.SaveChangesAsync(cancellationToken);
        return relationship;
    }
}
