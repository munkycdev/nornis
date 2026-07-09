using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record CreateCampaignCommand(
    Guid WorldId,
    string Name,
    Guid CreatingUserId,
    WorldRole CreatingUserRole,
    string? Description = null,
    CampaignStatus Status = CampaignStatus.Active,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null);
