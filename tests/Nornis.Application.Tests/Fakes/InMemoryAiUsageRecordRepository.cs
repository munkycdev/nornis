using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryAiUsageRecordRepository : IAiUsageRecordRepository
{
    private readonly List<AiUsageRecord> _records = [];

    public IReadOnlyList<AiUsageRecord> Records => _records.AsReadOnly();

    public Task<AiUsageRecord> CreateAsync(AiUsageRecord record, CancellationToken cancellationToken = default)
    {
        _records.Add(record);
        return Task.FromResult(record);
    }

    public Task<bool> AnySucceededAsync(
        Guid worldId,
        Guid userId,
        AiOperationType operationType,
        CancellationToken cancellationToken = default)
    {
        var any = _records.Any(r => r.WorldId == worldId
            && r.UserId == userId
            && r.OperationType == operationType
            && r.Succeeded);

        return Task.FromResult(any);
    }

    public Task<CostSummary> AggregateAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = _records.Where(r => r.WorldId == worldId);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);
        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        var records = query.ToList();
        if (records.Count == 0)
            return Task.FromResult(new CostSummary());

        return Task.FromResult(new CostSummary
        {
            TotalInputTokens = records.Sum(r => (long)r.InputTokens),
            TotalOutputTokens = records.Sum(r => (long)r.OutputTokens),
            TotalTokens = records.Sum(r => (long)r.TotalTokens),
            TotalEstimatedCostUsd = records.Sum(r => r.EstimatedCostUsd),
            OperationCount = records.Count
        });
    }

    public Task<decimal> SumPublicAskCostAsync(
        Guid worldId,
        DateTimeOffset fromInclusive,
        CancellationToken cancellationToken = default)
    {
        var sum = _records
            .Where(r => r.WorldId == worldId
                     && r.OperationType == AiOperationType.AskLoremaster
                     && r.UserId == null
                     && r.CreatedAt >= fromInclusive)
            .Sum(r => r.EstimatedCostUsd);

        return Task.FromResult(sum);
    }

    public Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByOperationTypeAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = FilterRecords(worldId, userId, fromDate, toDate);

        var result = query
            .GroupBy(r => r.OperationType.ToString())
            .Select(g => new GroupedCostSummary<string>
            {
                Key = g.Key,
                Summary = BuildSummary(g.ToList())
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<GroupedCostSummary<string>>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByModelAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = FilterRecords(worldId, userId, fromDate, toDate);

        var result = query
            .GroupBy(r => r.Model)
            .Select(g => new GroupedCostSummary<string>
            {
                Key = g.Key,
                Summary = BuildSummary(g.ToList())
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<GroupedCostSummary<string>>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByUserAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = FilterRecords(worldId, userId, fromDate, toDate);

        var result = query
            .Where(r => r.UserId.HasValue)
            .GroupBy(r => r.UserId!.Value)
            .Select(g => new GroupedCostSummary<Guid>
            {
                Key = g.Key,
                Summary = BuildSummary(g.ToList())
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<GroupedCostSummary<Guid>>>(result.AsReadOnly());
    }

    public Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByWorldAsync(
        IReadOnlyList<Guid> worldIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default)
    {
        var query = _records.Where(r => r.WorldId.HasValue && worldIds.Contains(r.WorldId.Value));

        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        var result = query
            .GroupBy(r => r.WorldId!.Value)
            .Select(g => new GroupedCostSummary<Guid>
            {
                Key = g.Key,
                Summary = BuildSummary(g.ToList())
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<GroupedCostSummary<Guid>>>(result.AsReadOnly());
    }

    private IEnumerable<AiUsageRecord> FilterRecords(
        Guid worldId, Guid? userId, DateTimeOffset? fromDate, DateTimeOffset? toDate)
    {
        var query = _records.Where(r => r.WorldId == worldId);

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);
        if (fromDate.HasValue)
            query = query.Where(r => r.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(r => r.CreatedAt <= toDate.Value);

        return query;
    }

    private static CostSummary BuildSummary(List<AiUsageRecord> records)
    {
        return new CostSummary
        {
            TotalInputTokens = records.Sum(r => (long)r.InputTokens),
            TotalOutputTokens = records.Sum(r => (long)r.OutputTokens),
            TotalTokens = records.Sum(r => (long)r.TotalTokens),
            TotalEstimatedCostUsd = records.Sum(r => r.EstimatedCostUsd),
            OperationCount = records.Count
        };
    }
}
