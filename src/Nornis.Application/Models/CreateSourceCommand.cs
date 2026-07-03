using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record CreateSourceCommand(
    Guid CampaignId,
    string Title,
    SourceType Type,
    VisibilityScope Visibility,
    Guid CreatingUserId,
    CampaignRole CreatingUserRole,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null);
