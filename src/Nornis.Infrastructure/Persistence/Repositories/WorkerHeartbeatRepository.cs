using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class WorkerHeartbeatRepository : IWorkerHeartbeatRepository
{
    private readonly NornisDbContext _context;

    public WorkerHeartbeatRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task BeatAsync(string workerName, DateTimeOffset beatAt, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Set<WorkerHeartbeat>()
            .FirstOrDefaultAsync(h => h.WorkerName == workerName, cancellationToken);

        if (existing is null)
        {
            _context.Set<WorkerHeartbeat>().Add(new WorkerHeartbeat { WorkerName = workerName, BeatAt = beatAt });
        }
        else
        {
            existing.BeatAt = beatAt;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two replicas booting together can both find no row and both insert; the key
            // collision means the other one got there first, which is the same news.
            _context.ChangeTracker.Clear();
        }
    }

    public async Task<DateTimeOffset?> GetLastBeatAsync(string workerName, CancellationToken cancellationToken = default)
    {
        return await _context.Set<WorkerHeartbeat>()
            .AsNoTracking()
            .Where(h => h.WorkerName == workerName)
            .Select(h => (DateTimeOffset?)h.BeatAt)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
