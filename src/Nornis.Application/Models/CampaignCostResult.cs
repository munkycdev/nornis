using Nornis.Domain.Models;

namespace Nornis.Application.Models;

public record CampaignCostResult
{
    public required Guid CampaignId { get; init; }
    public required string CampaignName { get; init; }
    public required CostSummary Summary { get; init; }
}
