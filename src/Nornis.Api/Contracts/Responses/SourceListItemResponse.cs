namespace Nornis.Api.Contracts.Responses;

public record SourceListItemResponse(
    Guid Id,
    Guid WorldId,
    string Type,
    string Title,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    string Visibility,
    string ProcessingStatus,
    Guid? CampaignId = null,
    string? CampaignName = null);
