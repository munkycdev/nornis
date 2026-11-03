using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Domain.Repositories;

public interface IAiUsageRecordRepository
{
    Task<AiUsageRecord> CreateAsync(AiUsageRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this user has ever completed the given operation successfully in this world.
    /// Backs the onboarding checklist, which polls every 15 seconds — loading the world's usage
    /// ledger to answer a boolean got more expensive with every AI call ever made.
    /// </summary>
    Task<bool> AnySucceededAsync(
        Guid worldId,
        Guid userId,
        AiOperationType operationType,
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

    /// <summary>
    /// Sum of estimated cost (USD) for a world since <paramref name="fromInclusive"/>, counting
    /// only the given operations. Backs the spend pools: the daily budget and the monthly Prep
    /// budget each sum their own operations. Returns 0 when nothing matches.
    /// </summary>
    Task<decimal> SumCostAsync(
        Guid worldId,
        DateTimeOffset fromInclusive,
        IReadOnlyCollection<AiOperationType> operations,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// What a user's trial has spent (feature 39): the sum of every record the trial funded with
    /// them as key holder, ever. The trial is one-time, so there is no window. Returns 0 when
    /// nothing matches.
    /// </summary>
    Task<decimal> SumTrialSpendAsync(Guid keyHolderUserId, CancellationToken cancellationToken = default);

    /// <summary>What every user's trial has spent since <paramref name="fromInclusive"/>, for the
    /// monthly ceiling across all users. Returns 0 when nothing matches.</summary>
    Task<decimal> SumAllTrialSpendAsync(DateTimeOffset fromInclusive, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GroupedCostSummary<string>>> AggregateByOperationTypeAsync(
        Guid worldId,
        Guid? userId,
        DateTimeOffset? fromDate,
        DateTimeOffset? toDate,
        CancellationToken cancellationToken = default);

    /// <summary>Costs by model, a model's estimated rows apart from its priced ones.</summary>
    Task<IReadOnlyList<GroupedCostSummary<ModelCostKey>>> AggregateByModelAsync(
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

    /// <summary>Costs by what paid (feature 39): one group per funding kind and key. Rows from
    /// before feature 39 have neither and group together.</summary>
    Task<IReadOnlyList<GroupedCostSummary<AiFundingKey>>> AggregateByFundingAsync(
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
