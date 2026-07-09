using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CostService : ICostService
{
    private readonly IAiUsageRecordRepository _aiUsageRecordRepository;
    private readonly IWorldMemberRepository _worldMemberRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly ILogger<CostService> _logger;

    public CostService(
        IAiUsageRecordRepository aiUsageRecordRepository,
        IWorldMemberRepository worldMemberRepository,
        IWorldRepository worldRepository,
        ILogger<CostService> logger)
    {
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _worldMemberRepository = worldMemberRepository;
        _worldRepository = worldRepository;
        _logger = logger;
    }

    public async Task<AppResult<TimePeriodCostResult>> GetSummaryAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var userIdFilter = DetermineUserIdFilter(userId, role);

        var todayRange = TimePeriodCalculator.GetTodayRange();
        var weekRange = TimePeriodCalculator.GetThisWeekRange();
        var monthRange = TimePeriodCalculator.GetThisMonthRange();

        // These aggregates must run sequentially: they share one scoped DbContext, and EF Core
        // forbids concurrent operations on a single context (Task.WhenAll here throws under the
        // relational provider's concurrency detector).
        var today = await _aiUsageRecordRepository.AggregateAsync(worldId, userIdFilter, todayRange.Start, todayRange.End, ct);
        var week = await _aiUsageRecordRepository.AggregateAsync(worldId, userIdFilter, weekRange.Start, weekRange.End, ct);
        var month = await _aiUsageRecordRepository.AggregateAsync(worldId, userIdFilter, monthRange.Start, monthRange.End, ct);
        var allTime = await _aiUsageRecordRepository.AggregateAsync(worldId, userIdFilter, null, null, ct);

        var result = new TimePeriodCostResult
        {
            Today = today,
            ThisWeek = week,
            ThisMonth = month,
            AllTime = allTime
        };

        sw.Stop();
        _logger.LogInformation(
            "Cost summary aggregation completed for world {WorldId} in {ElapsedMs}ms",
            worldId, sw.ElapsedMilliseconds);

        return AppResult<TimePeriodCostResult>.Success(result);
    }

    public async Task<AppResult<IReadOnlyList<WorldCostResult>>> GetByWorldAsync(
        Guid userId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var memberships = await _worldMemberRepository.ListByUserAsync(userId, ct);
        var gmWorldIds = memberships
            .Where(m => m.Role == WorldRole.GM)
            .Select(m => m.WorldId)
            .ToList();

        if (gmWorldIds.Count == 0)
        {
            sw.Stop();
            _logger.LogInformation(
                "Cost by-world aggregation completed for user {UserId} in {ElapsedMs}ms (no GM worlds)",
                userId, sw.ElapsedMilliseconds);

            return AppResult<IReadOnlyList<WorldCostResult>>.Success(
                Array.Empty<WorldCostResult>());
        }

        var groupedSummaries = await _aiUsageRecordRepository.AggregateByWorldAsync(gmWorldIds, null, null, ct);

        var worlds = await _worldRepository.GetByIdsAsync(gmWorldIds, ct);
        var worldNameMap = worlds.ToDictionary(c => c.Id, c => c.Name);

        var results = groupedSummaries
            .Select(g => new WorldCostResult
            {
                WorldId = g.Key,
                WorldName = worldNameMap.GetValueOrDefault(g.Key, "Unknown"),
                Summary = g.Summary
            })
            .ToList();

        sw.Stop();
        _logger.LogInformation(
            "Cost by-world aggregation completed for user {UserId} in {ElapsedMs}ms",
            userId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<WorldCostResult>>.Success(results);
    }

    public async Task<AppResult<IReadOnlyList<UserCostResult>>> GetByUserAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var validationError = ValidateDateRange(startDate, endDate);
        if (validationError is not null)
        {
            return AppResult<IReadOnlyList<UserCostResult>>.Fail(validationError);
        }

        var sw = Stopwatch.StartNew();

        var userIdFilter = DetermineUserIdFilter(userId, role);

        var groupedSummaries = await _aiUsageRecordRepository.AggregateByUserAsync(
            worldId, userIdFilter, startDate, endDate, ct);

        // Resolve usernames from world members
        var worldMembers = await _worldMemberRepository.ListByWorldAsync(worldId, ct);
        var usernameMap = worldMembers.ToDictionary(
            m => m.UserId,
            m => m.DisplayName ?? m.User?.Username ?? "Unknown");

        IReadOnlyList<UserCostResult> results;

        if (role != WorldRole.GM)
        {
            // Non-GM: return only the requesting user's summary
            var userEntry = groupedSummaries.FirstOrDefault(g => g.Key == userId);
            results = userEntry is not null
                ? new[]
                {
                    new UserCostResult
                    {
                        UserId = userId,
                        Username = usernameMap.GetValueOrDefault(userId, "Unknown"),
                        Summary = userEntry.Summary
                    }
                }
                : Array.Empty<UserCostResult>();
        }
        else
        {
            results = groupedSummaries
                .Select(g => new UserCostResult
                {
                    UserId = g.Key,
                    Username = usernameMap.GetValueOrDefault(g.Key, "Unknown"),
                    Summary = g.Summary
                })
                .ToList();
        }

        sw.Stop();
        _logger.LogInformation(
            "Cost by-user aggregation completed for world {WorldId} in {ElapsedMs}ms",
            worldId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<UserCostResult>>.Success(results);
    }

    public async Task<AppResult<IReadOnlyList<OperationTypeCostResult>>> GetByOperationTypeAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var validationError = ValidateDateRange(startDate, endDate);
        if (validationError is not null)
        {
            return AppResult<IReadOnlyList<OperationTypeCostResult>>.Fail(validationError);
        }

        var sw = Stopwatch.StartNew();

        var userIdFilter = DetermineUserIdFilter(userId, role);

        var groupedSummaries = await _aiUsageRecordRepository.AggregateByOperationTypeAsync(
            worldId, userIdFilter, startDate, endDate, ct);

        var results = groupedSummaries
            .Select(g => new OperationTypeCostResult
            {
                OperationType = g.Key,
                Summary = g.Summary
            })
            .ToList();

        sw.Stop();
        _logger.LogInformation(
            "Cost by-operation-type aggregation completed for world {WorldId} in {ElapsedMs}ms",
            worldId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<OperationTypeCostResult>>.Success(results);
    }

    public async Task<AppResult<IReadOnlyList<ModelCostResult>>> GetByModelAsync(
        Guid worldId,
        Guid userId,
        WorldRole role,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        CancellationToken ct)
    {
        var validationError = ValidateDateRange(startDate, endDate);
        if (validationError is not null)
        {
            return AppResult<IReadOnlyList<ModelCostResult>>.Fail(validationError);
        }

        var sw = Stopwatch.StartNew();

        var userIdFilter = DetermineUserIdFilter(userId, role);

        var groupedSummaries = await _aiUsageRecordRepository.AggregateByModelAsync(
            worldId, userIdFilter, startDate, endDate, ct);

        var results = groupedSummaries
            .Select(g => new ModelCostResult
            {
                Model = g.Key,
                Summary = g.Summary
            })
            .ToList();

        sw.Stop();
        _logger.LogInformation(
            "Cost by-model aggregation completed for world {WorldId} in {ElapsedMs}ms",
            worldId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<ModelCostResult>>.Success(results);
    }

    private static Guid? DetermineUserIdFilter(Guid userId, WorldRole role)
    {
        return role == WorldRole.GM ? null : userId;
    }

    private static AppError? ValidateDateRange(DateTimeOffset? startDate, DateTimeOffset? endDate)
    {
        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            return new AppError(400, "invalid_date_range", "Start date must be before or equal to end date.");
        }

        return null;
    }
}
