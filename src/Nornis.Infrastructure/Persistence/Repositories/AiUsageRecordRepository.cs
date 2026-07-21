using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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
        Guid? worldId = null,
        Guid? userId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        AiOperationType? operationType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AiUsageRecords.AsNoTracking().AsQueryable();

        if (worldId.HasValue)
        {
            query = query.Where(r => r.WorldId == worldId.Value);
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

    public async Task<CostSummary> AggregateAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(worldId, userId, fromDate, toDate);

        var result = await query
            .GroupBy(_ => 1)
            .Select(g => new CostSummary
            {
                TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
                TotalTokens = g.Sum(r => (long)r.TotalTokens),
                TotalEstimatedCostUsd = g.Sum(r => r.EstimatedCostUsd),
                OperationCount = g.Count()
            })
            .FirstOrDefaultAsync(cancellationToken);

        return result ?? CostSummary.Empty;
    }

    public async Task<decimal> SumPublicAskCostAsync(
        Guid worldId,
        DateTimeOffset fromInclusive,
        CancellationToken cancellationToken = default)
    {
        // Public asks are the only AskLoremaster rows with no user (members always carry one),
        // so this pair meters anonymous spend without a dedicated flag. Cast to decimal? so an
        // empty set sums to null → 0 rather than throwing.
        return await _context.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.WorldId == worldId
                     && r.OperationType == AiOperationType.AskLoremaster
                     && r.UserId == null
                     && r.CreatedAt >= fromInclusive)
            .SumAsync(r => (decimal?)r.EstimatedCostUsd, cancellationToken) ?? 0m;
    }

    public async Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByOperationTypeAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(worldId, userId, fromDate, toDate);

        // Group by the enum itself (it maps to the string column via the value converter).
        // Grouping by OperationType.ToString() cannot be translated by the relational provider,
        // so aggregate in SQL and convert the key to a string after materializing.
        var grouped = await query
            .GroupBy(r => r.OperationType)
            .Select(g => new
            {
                g.Key,
                TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
                TotalTokens = g.Sum(r => (long)r.TotalTokens),
                TotalEstimatedCostUsd = g.Sum(r => r.EstimatedCostUsd),
                OperationCount = g.Count()
            })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(g => new GroupedCostSummary<string>
            {
                Key = g.Key.ToString(),
                Summary = new CostSummary
                {
                    TotalInputTokens = g.TotalInputTokens,
                    TotalOutputTokens = g.TotalOutputTokens,
                    TotalTokens = g.TotalTokens,
                    TotalEstimatedCostUsd = g.TotalEstimatedCostUsd,
                    OperationCount = g.OperationCount
                }
            })
            .ToList();
    }

    public async Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByModelAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(worldId, userId, fromDate, toDate);

        var results = await query
            .GroupBy(r => r.Model)
            .Select(g => new GroupedCostSummary<string>
            {
                Key = g.Key,
                Summary = new CostSummary
                {
                    TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                    TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
                    TotalTokens = g.Sum(r => (long)r.TotalTokens),
                    TotalEstimatedCostUsd = g.Sum(r => r.EstimatedCostUsd),
                    OperationCount = g.Count()
                }
            })
            .ToListAsync(cancellationToken);

        return results;
    }

    public async Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByUserAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = BuildFilteredQuery(worldId, userId, fromDate, toDate)
            .Where(r => r.UserId != null);

        var results = await query
            .GroupBy(r => r.UserId!.Value)
            .Select(g => new GroupedCostSummary<Guid>
            {
                Key = g.Key,
                Summary = new CostSummary
                {
                    TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                    TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
                    TotalTokens = g.Sum(r => (long)r.TotalTokens),
                    TotalEstimatedCostUsd = g.Sum(r => r.EstimatedCostUsd),
                    OperationCount = g.Count()
                }
            })
            .ToListAsync(cancellationToken);

        return results;
    }

    public async Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByWorldAsync(
        IReadOnlyList<Guid> worldIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = _context.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.WorldId != null && worldIds.Contains(r.WorldId.Value));

        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        var results = await query
            .GroupBy(r => r.WorldId!.Value)
            .Select(g => new GroupedCostSummary<Guid>
            {
                Key = g.Key,
                Summary = new CostSummary
                {
                    TotalInputTokens = g.Sum(r => (long)r.InputTokens),
                    TotalOutputTokens = g.Sum(r => (long)r.OutputTokens),
                    TotalTokens = g.Sum(r => (long)r.TotalTokens),
                    TotalEstimatedCostUsd = g.Sum(r => r.EstimatedCostUsd),
                    OperationCount = g.Count()
                }
            })
            .ToListAsync(cancellationToken);

        return results;
    }

    private IQueryable<AiUsageRecord> BuildFilteredQuery(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate)
    {
        var query = _context.AiUsageRecords
            .AsNoTracking()
            .Where(r => r.WorldId == worldId);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);

        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        return query;
    }
}
