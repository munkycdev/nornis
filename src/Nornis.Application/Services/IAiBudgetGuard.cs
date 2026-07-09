using Nornis.Application.Errors;

namespace Nornis.Application.Services;

public record AiBudgetStatus(decimal SpentTodayUsd, decimal DailyBudgetUsd, bool IsExceeded);

/// <summary>
/// Campaign-level daily AI spend ceiling. Every AI entry point (ask, extraction,
/// continuity audit) checks this before calling the model, so a misbehaving loop or an
/// enthusiastic table can't quietly burn money past the configured cap.
/// </summary>
public interface IAiBudgetGuard
{
    Task<AiBudgetStatus> GetStatusAsync(Guid campaignId, CancellationToken ct);

    /// <summary>Null when spending is allowed; a 429 AppError when today's budget is spent.</summary>
    Task<AppError?> CheckAsync(Guid campaignId, CancellationToken ct);
}
