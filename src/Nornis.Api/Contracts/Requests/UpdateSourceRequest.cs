namespace Nornis.Api.Contracts.Requests;

public record UpdateSourceRequest(
    string? Title = null,
    string? Body = null,
    bool ClearBody = false,
    string? Uri = null,
    bool ClearUri = false,
    DateTimeOffset? OccurredAt = null,
    bool ClearOccurredAt = false,
    string? Type = null,
    string? Visibility = null,
    Guid? CampaignId = null,
    bool ClearCampaign = false,
    bool? ExtractionEnabled = null);
