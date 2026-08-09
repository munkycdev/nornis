namespace Nornis.Api.Contracts.Responses;

public record WorldResponse(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? MyRole = null,
    decimal? DailyAiBudgetUsd = null,
    string? PublicSlug = null,
    bool PublicAccessEnabled = false,
    decimal? PublicAskMonthlyBudgetUsd = null,
    bool SummaryReviewRequired = false,
    bool IsDemo = false,
    bool TutorialEnabled = false,
    bool IsTemplate = false,
    /// <summary>The campaign the world is playing now; null when none is. Carried on the world
    /// so the capture form can default to it without a second round trip.</summary>
    Guid? CurrentCampaignId = null);
