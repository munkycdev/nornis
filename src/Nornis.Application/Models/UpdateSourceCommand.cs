using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateSourceCommand(
    Guid SourceId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    SourceType? Type = null,
    VisibilityScope? Visibility = null,
    Guid? CampaignId = null,
    bool ClearCampaign = false,
    bool? ExtractionEnabled = null);
