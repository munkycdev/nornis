using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateCampaignCommand(
    Guid CampaignId,
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    string? Name = null,
    string? Description = null,
    CampaignStatus? Status = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndedAt = null);
