namespace Nornis.Api.Contracts.Responses;

public record CampaignCostResponse(
    Guid CampaignId,
    string CampaignName,
    CostSummaryResponse Summary);
