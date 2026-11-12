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
    bool SummaryReviewRequired = false,
    bool IsDemo = false,
    bool TutorialEnabled = false,
    bool IsTemplate = false,
    /// <summary>The campaign the world is playing now; null when none is. Carried on the world
    /// so the capture form can default to it without a second round trip.</summary>
    Guid? CurrentCampaignId = null,
    /// <summary>Prep's monthly AI cap for this world; null inherits the configured default.</summary>
    decimal? PrepMonthlyBudgetUsd = null,
    /// <summary>How many questions a check-in asks on Home (feature 44).</summary>
    int CheckInQuestionCount = 5);
