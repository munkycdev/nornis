using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class AiUsageRecordRepository : IAiUsageRecordRepository
{
    private readonly NornisDbContext _context;

    public AiUsageRecordRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<AiUsageRecord> CreateAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
    {
        _context.AiUsageRecords.Add(record);
        await _context.SaveChangesAsync(cancellationToken);
        return record;
    }

    public async Task<IReadOnlyList<AiUsageRecord>> QueryAsync(
        Guid? campaignId = null,
        Guid? userId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        AiOperationType? operationType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AiUsageRecords.AsNoTracking().AsQueryable();

        if (campaignId.HasValue)
        {
            query = query.Where(r => r.CampaignId == campaignId.Value);
        }

        if (userId.HasValue)
        {
            query = query.Where(r => r.UserId == userId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(r => r.CreatedAt <= toDate.Value);
        }

        if (operationType.HasValue)
        {
            query = query.Where(r => r.OperationType == operationType.Value);
        }

        return await query.ToListAsync(cancellationToken);
    }
}
