using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateWorldCommand(
    Guid WorldId,
    string? Name,
    string? Description,
    string? GameSystem,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    decimal? DailyAiBudgetUsd = null,
    bool ClearDailyAiBudget = false,
    string? PublicSlug = null,
    bool? PublicAccessEnabled = null,
    decimal? PublicAskMonthlyBudgetUsd = null,
    bool ClearPublicAskBudget = false,
    bool? IsTemplate = null);
