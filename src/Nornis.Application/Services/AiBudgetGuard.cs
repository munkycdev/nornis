using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Errors;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class AiBudgetGuard : IAiBudgetGuard
{
    private readonly IAiUsageRecordRepository _usageRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly AiBudgetOptions _options;

    public AiBudgetGuard(
        IAiUsageRecordRepository usageRepository,
        IWorldRepository worldRepository,
        IOptions<AiBudgetOptions> options)
    {
        _usageRepository = usageRepository;
        _worldRepository = worldRepository;
        _options = options.Value;
    }

    public async Task<AiBudgetStatus> GetStatusAsync(Guid worldId, CancellationToken ct)
    {
        // A world-level override wins over the configured default.
        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        var budget = world?.DailyAiBudgetUsd ?? _options.DailyWorldBudgetUsd;
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

    public async Task<PublicAskBudgetStatus> GetPublicAskStatusAsync(Guid worldId, CancellationToken ct)
    {
        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        var budget = world?.PublicAskMonthlyBudgetUsd ?? 0m;

        // The cap is also the switch: no positive cap means public Ask is off.
        if (budget <= 0m)
            return new PublicAskBudgetStatus(IsEnabled: false, MonthlyBudgetUsd: 0m, SpentThisMonthUsd: 0m, IsExceeded: false);

        var now = DateTime.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var spent = await _usageRepository.SumPublicAskCostAsync(worldId, monthStart, ct);

        return new PublicAskBudgetStatus(
            IsEnabled: true,
            MonthlyBudgetUsd: budget,
            SpentThisMonthUsd: spent,
            IsExceeded: spent >= budget);
    }

    public async Task<AppError?> CheckPublicAskAsync(Guid worldId, CancellationToken ct)
    {
        var status = await GetPublicAskStatusAsync(worldId, ct);

        if (!status.IsEnabled)
            return new AppError(404, "public_ask_unavailable",
                "Asking the Loremaster isn't enabled for this world.");

        if (status.IsExceeded)
            return new AppError(429, "public_ask_budget_exceeded",
                "This world's public question budget for the month has been reached. It resets at the start of next month.");

        return null;
    }
}
