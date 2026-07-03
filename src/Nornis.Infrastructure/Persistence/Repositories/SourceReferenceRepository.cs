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
            .Where(sr => targetIds.Contains(sr.TargetId))
            .ToListAsync(cancellationToken);
    }
}
