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
    private readonly ICampaignMemberRepository _campaignMemberRepository;
    private readonly ICampaignRepository _campaignRepository;
    private readonly ILogger<CostService> _logger;

    public CostService(
        IAiUsageRecordRepository aiUsageRecordRepository,
        ICampaignMemberRepository campaignMemberRepository,
        ICampaignRepository campaignRepository,
        ILogger<CostService> logger)
    {
        _aiUsageRecordRepository = aiUsageRecordRepository;
        _campaignMemberRepository = campaignMemberRepository;
        _campaignRepository = campaignRepository;
        _logger = logger;
    }

    public async Task<AppResult<TimePeriodCostResult>> GetSummaryAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var userIdFilter = DetermineUserIdFilter(userId, role);

        var todayRange = TimePeriodCalculator.GetTodayRange();
        var weekRange = TimePeriodCalculator.GetThisWeekRange();
        var monthRange = TimePeriodCalculator.GetThisMonthRange();

        var todayTask = _aiUsageRecordRepository.AggregateAsync(campaignId, userIdFilter, todayRange.Start, todayRange.End, ct);
        var weekTask = _aiUsageRecordRepository.AggregateAsync(campaignId, userIdFilter, weekRange.Start, weekRange.End, ct);
        var monthTask = _aiUsageRecordRepository.AggregateAsync(campaignId, userIdFilter, monthRange.Start, monthRange.End, ct);
        var allTimeTask = _aiUsageRecordRepository.AggregateAsync(campaignId, userIdFilter, null, null, ct);

        await Task.WhenAll(todayTask, weekTask, monthTask, allTimeTask);

        var result = new TimePeriodCostResult
        {
            Today = todayTask.Result,
            ThisWeek = weekTask.Result,
            ThisMonth = monthTask.Result,
            AllTime = allTimeTask.Result
        };

        sw.Stop();
        _logger.LogInformation(
            "Cost summary aggregation completed for campaign {CampaignId} in {ElapsedMs}ms",
            campaignId, sw.ElapsedMilliseconds);

        return AppResult<TimePeriodCostResult>.Success(result);
    }

    public async Task<AppResult<IReadOnlyList<CampaignCostResult>>> GetByCampaignAsync(
        Guid userId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var memberships = await _campaignMemberRepository.ListByUserAsync(userId, ct);
        var gmCampaignIds = memberships
            .Where(m => m.Role == CampaignRole.GM)
            .Select(m => m.CampaignId)
            .ToList();

        if (gmCampaignIds.Count == 0)
        {
            sw.Stop();
            _logger.LogInformation(
                "Cost by-campaign aggregation completed for user {UserId} in {ElapsedMs}ms (no GM campaigns)",
                userId, sw.ElapsedMilliseconds);

            return AppResult<IReadOnlyList<CampaignCostResult>>.Success(
                Array.Empty<CampaignCostResult>());
        }

        var groupedSummaries = await _aiUsageRecordRepository.AggregateByCampaignAsync(gmCampaignIds, null, null, ct);

        var campaigns = await _campaignRepository.GetByIdsAsync(gmCampaignIds, ct);
        var campaignNameMap = campaigns.ToDictionary(c => c.Id, c => c.Name);

        var results = groupedSummaries
            .Select(g => new CampaignCostResult
            {
                CampaignId = g.Key,
                CampaignName = campaignNameMap.GetValueOrDefault(g.Key, "Unknown"),
                Summary = g.Summary
            })
            .ToList();

        sw.Stop();
        _logger.LogInformation(
            "Cost by-campaign aggregation completed for user {UserId} in {ElapsedMs}ms",
            userId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<CampaignCostResult>>.Success(results);
    }

    public async Task<AppResult<IReadOnlyList<UserCostResult>>> GetByUserAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
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
            campaignId, userIdFilter, startDate, endDate, ct);

        // Resolve usernames from campaign members
        var campaignMembers = await _campaignMemberRepository.ListByCampaignAsync(campaignId, ct);
        var usernameMap = campaignMembers.ToDictionary(
            m => m.UserId,
            m => m.DisplayName ?? m.User?.Username ?? "Unknown");

        IReadOnlyList<UserCostResult> results;

        if (role != CampaignRole.GM)
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
            "Cost by-user aggregation completed for campaign {CampaignId} in {ElapsedMs}ms",
            campaignId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<UserCostResult>>.Success(results);
    }

    public async Task<AppResult<IReadOnlyList<OperationTypeCostResult>>> GetByOperationTypeAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
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
            campaignId, userIdFilter, startDate, endDate, ct);

        var results = groupedSummaries
            .Select(g => new OperationTypeCostResult
            {
                OperationType = g.Key,
                Summary = g.Summary
            })
            .ToList();

        sw.Stop();
        _logger.LogInformation(
            "Cost by-operation-type aggregation completed for campaign {CampaignId} in {ElapsedMs}ms",
            campaignId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<OperationTypeCostResult>>.Success(results);
    }

    public async Task<AppResult<IReadOnlyList<ModelCostResult>>> GetByModelAsync(
        Guid campaignId,
        Guid userId,
        CampaignRole role,
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
            campaignId, userIdFilter, startDate, endDate, ct);

        var results = groupedSummaries
            .Select(g => new ModelCostResult
            {
                Model = g.Key,
                Summary = g.Summary
            })
            .ToList();

        sw.Stop();
        _logger.LogInformation(
            "Cost by-model aggregation completed for campaign {CampaignId} in {ElapsedMs}ms",
            campaignId, sw.ElapsedMilliseconds);

        return AppResult<IReadOnlyList<ModelCostResult>>.Success(results);
    }

    private static Guid? DetermineUserIdFilter(Guid userId, CampaignRole role)
    {
        return role == CampaignRole.GM ? null : userId;
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
