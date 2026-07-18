using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record CreateSourceCommand(
    Guid WorldId,
    string Title,
    SourceType Type,
    VisibilityScope Visibility,
    Guid CreatingUserId,
    WorldRole CreatingUserRole,
    string? Body = null,
    string? Uri = null,
    DateTimeOffset? OccurredAt = null,
    Guid? CampaignId = null,
    bool ExtractionEnabled = true);
