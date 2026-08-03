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
    private readonly IAiPauseGate _pauseGate;

    public AiBudgetGuard(
        IAiUsageRecordRepository usageRepository,
        IWorldRepository worldRepository,
        IOptions<AiBudgetOptions> options,
        IAiPauseGate pauseGate)
    {
        _usageRepository = usageRepository;
        _worldRepository = worldRepository;
        _options = options.Value;
        _pauseGate = pauseGate;
    }

    public async Task<AiBudgetStatus> GetStatusAsync(Guid worldId, CancellationToken ct)
    {
        // A world-level override wins over the configured default; null inherits, exactly as a
        // null public-Ask cap does below.
        var world = await _worldRepository.GetByIdAsync(worldId, ct);
        var budget = world?.DailyAiBudgetUsd ?? _options.DailyWorldBudgetUsd;

        // Only an absent ceiling means "spend freely". A zero is a ceiling of zero, and falls
        // through to be exceeded by any spend at all — the same reading the public-Ask cap gives
        // it. This used to be `<= 0`, so the two caps disagreed about what zero meant, and a zero
        // reaching WorldService's 0.01 floor by some future route would have opened this guard
        // rather than closed it.
        if (budget is not { } ceiling)
            return new AiBudgetStatus(0m, 0m, IsExceeded: false);

        var todayUtc = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var summary = await _usageRepository.AggregateAsync(worldId, null, todayUtc, null, ct);

        return new AiBudgetStatus(
            SpentTodayUsd: summary.TotalEstimatedCostUsd,
            DailyBudgetUsd: ceiling,
            IsExceeded: summary.TotalEstimatedCostUsd >= ceiling);
    }

    public async Task<AppError?> CheckAsync(Guid worldId, CancellationToken ct)
    {
        // The global pause is checked before the budget, and here rather than at each of the
        // eight services that spend money: every paid dispatch already calls this method, so
        // this is the one seam that reaches all of them without touching any of them.
        if (await _pauseGate.GetAsync(ct) is { IsPaused: true } paused)
        {
            return PausedError(paused);
        }

        var status = await GetStatusAsync(worldId, ct);
        if (!status.IsExceeded)
            return null;

        return new AppError(429, "ai_budget_exceeded",
            $"This world's daily AI budget (${status.DailyBudgetUsd:0.00}) is spent for today. It resets at midnight UTC.");
    }

    /// <summary>
    /// 503, not 429: a pause is the service being deliberately unavailable, not the caller
    /// having asked too often. Retry-After is meaningless here — nobody knows when an operator
    /// will flip it back — so the reason they typed is what the user gets instead.
    /// </summary>
    private static AppError PausedError(AiPauseState paused) =>
        new(503, "ai_paused", string.IsNullOrWhiteSpace(paused.Reason)
            ? "AI features are paused. Try again shortly."
            : $"AI features are paused: {paused.Reason}");

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
        if (await _pauseGate.GetAsync(ct) is { IsPaused: true } paused)
        {
            return PausedError(paused);
        }

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
