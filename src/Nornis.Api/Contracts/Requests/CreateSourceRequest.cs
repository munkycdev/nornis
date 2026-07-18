namespace Nornis.Api.Contracts.Requests;

public record CreateSourceRequest(
    string Title,
    string Type,
    string Visibility,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    Guid? CampaignId = null,
    bool ExtractionEnabled = true);
