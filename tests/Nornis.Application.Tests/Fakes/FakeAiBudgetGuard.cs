using Nornis.Application.Errors;
using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Budget guard fake. Defaults to unlimited; set <see cref="Exceeded"/> to simulate a
/// spent daily budget, or the public-ask flags to simulate the monthly public cap.
/// </summary>
public class FakeAiBudgetGuard : IAiBudgetGuard
{
    public bool Exceeded { get; set; }
    public decimal SpentTodayUsd { get; set; }
    public decimal DailyBudgetUsd { get; set; } = 2.00m;

    public bool PublicAskEnabled { get; set; } = true;
    public bool PublicAskExceeded { get; set; }
    public decimal PublicAskMonthlyBudgetUsd { get; set; } = 10.00m;
    public decimal PublicAskSpentThisMonthUsd { get; set; }

    public Task<AiBudgetStatus> GetStatusAsync(Guid worldId, CancellationToken ct) =>
        Task.FromResult(new AiBudgetStatus(SpentTodayUsd, DailyBudgetUsd, Exceeded));

    public Task<AppError?> CheckAsync(Guid worldId, CancellationToken ct) =>
        Task.FromResult<AppError?>(Exceeded
            ? new AppError(429, "ai_budget_exceeded", "This world's daily AI budget is spent for today.")
            : null);

    public Task<PublicAskBudgetStatus> GetPublicAskStatusAsync(Guid worldId, CancellationToken ct) =>
        Task.FromResult(new PublicAskBudgetStatus(
            PublicAskEnabled,
            PublicAskEnabled ? PublicAskMonthlyBudgetUsd : 0m,
            PublicAskSpentThisMonthUsd,
            PublicAskEnabled && PublicAskExceeded));

    public Task<AppError?> CheckPublicAskAsync(Guid worldId, CancellationToken ct) =>
        Task.FromResult<AppError?>(!PublicAskEnabled
            ? new AppError(404, "public_ask_unavailable", "Asking the Loremaster isn't enabled for this world.")
            : PublicAskExceeded
                ? new AppError(429, "public_ask_budget_exceeded", "This world's public question budget for the month has been reached.")
                : null);
}
