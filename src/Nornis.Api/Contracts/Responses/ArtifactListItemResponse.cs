namespace Nornis.Api.Contracts.Responses;

public record ArtifactListItemResponse(
    Guid Id,
    Guid CampaignId,
    string Type,
    string Name,
    string? Summary,
    string Status,
    string Visibility,
    decimal? Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
