using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IAiUsageRecordRepository
{
    Task<AiUsageRecord> CreateAsync(AiUsageRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiUsageRecord>> QueryAsync(
        Guid? campaignId = null,
        Guid? userId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        AiOperationType? operationType = null,
        CancellationToken cancellationToken = default);

    Task<CostSummary> AggregateAsync(
        Guid campaignId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByOperationTypeAsync(
        Guid campaignId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByModelAsync(
        Guid campaignId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByUserAsync(
        Guid campaignId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByCampaignAsync(
        IReadOnlyList<Guid> campaignIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);
}
