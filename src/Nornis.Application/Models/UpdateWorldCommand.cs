namespace Nornis.Application.Models;

public record UpdateWorldCommand(
    Guid WorldId,
    string? Name,
    string? Description,
    string? GameSystem,
    Guid ActingUserId,
    decimal? DailyAiBudgetUsd = null,
    bool ClearDailyAiBudget = false,
    string? PublicSlug = null,
    bool? PublicAccessEnabled = null);
