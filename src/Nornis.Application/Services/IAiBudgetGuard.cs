using Nornis.Application.Errors;

namespace Nornis.Application.Services;

public record AiBudgetStatus(decimal SpentTodayUsd, decimal DailyBudgetUsd, bool IsExceeded);

/// <summary>
/// Public "Ask the Loremaster" spend, this calendar month, against the GM-configured cap.
/// <see cref="IsEnabled"/> is false when no positive cap is set — public Ask is off by default.
/// </summary>
public record PublicAskBudgetStatus(
    bool IsEnabled,
    decimal MonthlyBudgetUsd,
    decimal SpentThisMonthUsd,
    bool IsExceeded);

/// <summary>
/// World-level daily AI spend ceiling. Every AI entry point (ask, extraction,
/// continuity audit) checks this before calling the model, so a misbehaving loop or an
/// enthusiastic table can't quietly burn money past the configured cap.
/// </summary>
public interface IAiBudgetGuard
{
    Task<AiBudgetStatus> GetStatusAsync(Guid worldId, CancellationToken ct);

    /// <summary>Null when spending is allowed; a 429 AppError when today's budget is spent.</summary>
    Task<AppError?> CheckAsync(Guid worldId, CancellationToken ct);

    /// <summary>
    /// The public Ask cap status for a world: whether anonymous Ask is enabled at all, and how
    /// much of this month's cap has been spent.
    /// </summary>
    Task<PublicAskBudgetStatus> GetPublicAskStatusAsync(Guid worldId, CancellationToken ct);

    /// <summary>
    /// Null when an anonymous public ask may proceed; a 404 AppError when public Ask is disabled
    /// for the world, or a 429 when this month's public cap is spent.
    /// </summary>
    Task<AppError?> CheckPublicAskAsync(Guid worldId, CancellationToken ct);
}
