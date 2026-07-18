namespace Nornis.Api.Contracts.Requests;

public record UpdateSourceRequest(
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    string? Type = null,
    string? Visibility = null,
    Guid? CampaignId = null,
    bool ClearCampaign = false,
    bool? ExtractionEnabled = null);
