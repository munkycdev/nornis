using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class SourceReferenceRepository : ISourceReferenceRepository
{
    private readonly NornisDbContext _context;

    public SourceReferenceRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<SourceReference> CreateAsync(SourceReference reference, CancellationToken cancellationToken = default)
    {
        _context.SourceReferences.Add(reference);
        await _context.SaveChangesAsync(cancellationToken);
        return reference;
    }

    public async Task<IReadOnlyList<SourceReference>> ListByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        return await _context.SourceReferences
            .AsNoTracking()
            .Where(sr => sr.TargetType == targetType && sr.TargetId == targetId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceReference>> ListByTargetIdsAsync(IReadOnlyList<Guid> targetIds, CancellationToken cancellationToken = default)
    {
        if (targetIds.Count == 0)
            return [];

        return await _context.SourceReferences
            .AsNoTracking()
            .Include(sr => sr.Source)
            .Where(sr => targetIds.Contains(sr.TargetId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SourceReference>> ListBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        return await _context.SourceReferences
            .AsNoTracking()
            .Where(sr => sr.SourceId == sourceId)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteBySourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        // Tracked-load delete: the InMemory provider used in tests lacks ExecuteDelete.
        var references = await _context.SourceReferences
            .Where(sr => sr.SourceId == sourceId)
            .ToListAsync(cancellationToken);

        if (references.Count == 0)
        {
            return;
        }

        _context.SourceReferences.RemoveRange(references);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        var references = await _context.SourceReferences
            .Where(sr => sr.TargetType == targetType && sr.TargetId == targetId)
            .ToListAsync(cancellationToken);

        if (references.Count == 0)
        {
            return;
        }

        _context.SourceReferences.RemoveRange(references);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteBySourceAndTargetAsync(Guid sourceId, SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default)
    {
        // Tracked-load delete: the InMemory provider used in tests lacks ExecuteDelete.
        var references = await _context.SourceReferences
            .Where(sr => sr.SourceId == sourceId && sr.TargetType == targetType && sr.TargetId == targetId)
            .ToListAsync(cancellationToken);

        if (references.Count == 0)
        {
            return;
        }

        _context.SourceReferences.RemoveRange(references);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
