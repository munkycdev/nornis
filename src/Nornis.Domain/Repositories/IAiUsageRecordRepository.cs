using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IAiUsageRecordRepository
{
    Task<AiUsageRecord> CreateAsync(AiUsageRecord record, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AiUsageRecord>> QueryAsync(
        Guid? worldId = null,
        Guid? userId = null,
        DateTimeOffset? fromDate = null,
        DateTimeOffset? toDate = null,
        AiOperationType? operationType = null,
        CancellationToken cancellationToken = default);

    Task<CostSummary> AggregateAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sum of estimated cost (USD) for anonymous public "Ask the Loremaster" calls against a
    /// world since <paramref name="fromInclusive"/>. Public Ask records are the only
    /// <see cref="AiOperationType.AskLoremaster"/> rows with no <c>UserId</c> — members always
    /// carry one — so that pair is an exact meter for the public monthly cap. Returns 0 when
    /// there are no matching rows.
    /// </summary>
    Task<decimal> SumPublicAskCostAsync(
        Guid worldId,
        DateTimeOffset fromInclusive,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByOperationTypeAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByModelAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByUserAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<Guid>>> AggregateByWorldAsync(
        IReadOnlyList<Guid> worldIds,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);
}
