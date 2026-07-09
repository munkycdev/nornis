using Nornis.Application.Errors;
using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// Budget guard fake. Defaults to unlimited; set <see cref="Exceeded"/> to simulate a
/// spent daily budget.
/// </summary>
public class FakeAiBudgetGuard : IAiBudgetGuard
{
    public bool Exceeded { get; set; }
    public decimal SpentTodayUsd { get; set; }
    public decimal DailyBudgetUsd { get; set; } = 2.00m;

    public Task<AiBudgetStatus> GetStatusAsync(Guid worldId, CancellationToken ct) =>
        Task.FromResult(new AiBudgetStatus(SpentTodayUsd, DailyBudgetUsd, Exceeded));

    public Task<AppError?> CheckAsync(Guid worldId, CancellationToken ct) =>
        Task.FromResult<AppError?>(Exceeded
            ? new AppError(429, "ai_budget_exceeded", "This world's daily AI budget is spent for today.")
            : null);
}
