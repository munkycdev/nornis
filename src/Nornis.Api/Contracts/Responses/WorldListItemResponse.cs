namespace Nornis.Api.Contracts.Responses;

public record WorldListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    string MyRole,
    string? PublicSlug = null,
    bool PublicAccessEnabled = false,
    decimal? DailyAiBudgetUsd = null,
    decimal? PublicAskMonthlyBudgetUsd = null,
    bool IsDemo = false,
    bool TutorialEnabled = false,
    bool IsTemplate = false);
