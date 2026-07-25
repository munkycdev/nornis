using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class TutorialProgressRepository : ITutorialProgressRepository
{
    private readonly NornisDbContext _context;

    public TutorialProgressRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TutorialProgress>> ListAsync(Guid userId, Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.Set<TutorialProgress>()
            .AsNoTracking()
            .Where(p => p.UserId == userId && p.WorldId == worldId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IReadOnlyList<TutorialProgress> entries, CancellationToken cancellationToken = default)
    {
        if (entries.Count == 0)
        {
            return;
        }

        _context.Set<TutorialProgress>().AddRange(entries);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A concurrent request detected the same step first — the unique index makes
            // the duplicate insert fail, and completion is completion either way.
        }
    }
}
