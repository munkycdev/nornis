using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ContinuityDismissalRepository : IContinuityDismissalRepository
{
    private readonly NornisDbContext _context;

    public ContinuityDismissalRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ContinuityDismissal> CreateAsync(
        ContinuityDismissal dismissal, CancellationToken cancellationToken = default)
    {
        _context.ContinuityDismissals.Add(dismissal);
        await _context.SaveChangesAsync(cancellationToken);
        return dismissal;
    }

    public async Task<IReadOnlyList<ContinuityDismissal>> ListByWorldAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ContinuityDismissals
            .AsNoTracking()
            .Where(d => d.WorldId == worldId)
            .OrderBy(d => d.DismissedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
