using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class AiBudgetGuard : IAiBudgetGuard
{
    private readonly IAiUsageRecordRepository _usageRepository;
    private readonly AiBudgetOptions _options;

    public AiBudgetGuard(IAiUsageRecordRepository usageRepository, IOptions<AiBudgetOptions> options)
    {
        _usageRepository = usageRepository;
        _options = options.Value;
    }

    public async Task<AiBudgetStatus> GetStatusAsync(Guid worldId, CancellationToken ct)
    {
        var budget = _options.DailyWorldBudgetUsd;
        if (budget <= 0)
            return new AiBudgetStatus(0m, 0m, IsExceeded: false);

        var todayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var summary = await _usageRepository.AggregateAsync(worldId, null, todayUtc, null, ct);

        return new AiBudgetStatus(
            SpentTodayUsd: summary.TotalEstimatedCostUsd,
            DailyBudgetUsd: budget,
            IsExceeded: summary.TotalEstimatedCostUsd >= budget);
    }

    public async Task<AppError?> CheckAsync(Guid worldId, CancellationToken ct)
    {
        var status = await GetStatusAsync(worldId, ct);
        if (!status.IsExceeded)
            return null;

        return new AppError(429, "ai_budget_exceeded",
            $"This world's daily AI budget (${status.DailyBudgetUsd:0.00}) is spent for today. It resets at midnight UTC.");
    }
}
