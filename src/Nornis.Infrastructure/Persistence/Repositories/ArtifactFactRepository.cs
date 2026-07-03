using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ArtifactFactRepository : IArtifactFactRepository
{
    private readonly NornisDbContext _context;

    public ArtifactFactRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ArtifactFact> CreateAsync(ArtifactFact fact, CancellationToken cancellationToken = default)
    {
        _context.ArtifactFacts.Add(fact);
        await _context.SaveChangesAsync(cancellationToken);
        return fact;
    }

    public async Task<ArtifactFact?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ArtifactFacts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ArtifactFact>> ListByArtifactAsync(Guid artifactId, CancellationToken cancellationToken = default)
    {
        return await _context.ArtifactFacts
            .AsNoTracking()
            .Where(f => f.ArtifactId == artifactId)
            .ToListAsync(cancellationToken);
    }

    public async Task<ArtifactFact> UpdateAsync(ArtifactFact fact, CancellationToken cancellationToken = default)
    {
        _context.ArtifactFacts.Update(fact);
        await _context.SaveChangesAsync(cancellationToken);
        return fact;
    }

    public async Task<IReadOnlyList<ArtifactFact>> ListByArtifactIdsAsync(
        IReadOnlyList<Guid> artifactIds,
        int maxPerArtifact,
        CancellationToken cancellationToken = default)
    {
        if (artifactIds.Count == 0)
            return [];

        var facts = await _context.ArtifactFacts
            .AsNoTracking()
            .Where(f => artifactIds.Contains(f.ArtifactId))
            .OrderByDescending(f => f.UpdatedAt)
            .ToListAsync(cancellationToken);

        return facts
            .GroupBy(f => f.ArtifactId)
            .SelectMany(g => g.Take(maxPerArtifact))
            .ToList();
    }
}
