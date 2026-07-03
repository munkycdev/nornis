using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateSourceCommand(
    Guid SourceId,
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole,
    string? Title = null,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    SourceType? Type = null,
    VisibilityScope? Visibility = null);
