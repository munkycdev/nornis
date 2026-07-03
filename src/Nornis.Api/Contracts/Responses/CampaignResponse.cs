namespace Nornis.Api.Contracts.Responses;

public record CampaignResponse(
    Guid Id,
    string Name,
    string? Description,
    string? GameSystem,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? MyRole = null);
