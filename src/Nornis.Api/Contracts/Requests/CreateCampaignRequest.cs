namespace Nornis.Api.Contracts.Requests;

public record CreateCampaignRequest(
    string Name,
    string? Description = null,
    string? GameSystem = null);
