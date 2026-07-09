namespace Nornis.Api.Contracts.Responses;

public record CampaignResponse(
    Guid Id,
    Guid WorldId,
    string Name,
    string? Description,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid CreatedByUserId);
