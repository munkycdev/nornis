using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ExtractionReplayRepository : IExtractionReplayRepository
{
    private readonly NornisDbContext _context;

    public ExtractionReplayRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ExtractionReplay> CreateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default)
    {
        _context.ExtractionReplays.Add(replay);
        await _context.SaveChangesAsync(cancellationToken);
        return replay;
    }

    public async Task<ExtractionReplay?> GetActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ExtractionReplays
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.WorldId == worldId && r.Status == ExtractionReplayStatus.Active,
                cancellationToken);
    }

    public async Task<ExtractionReplay> UpdateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default)
    {
        _context.ExtractionReplays.Update(replay);
        await _context.SaveChangesAsync(cancellationToken);
        return replay;
    }
}
