namespace Nornis.Api.Contracts.Responses;

public record CampaignListItemResponse(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    string MyRole);
