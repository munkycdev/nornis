namespace Nornis.Api.Contracts.Responses;

public record SourceListItemResponse(
    Guid Id,
    Guid CampaignId,
    string Type,
    string Title,
    DateTimeOffset? OccurredAt,
    DateTimeOffset CreatedAt,
    Guid CreatedByUserId,
    string Visibility,
    string ProcessingStatus);
