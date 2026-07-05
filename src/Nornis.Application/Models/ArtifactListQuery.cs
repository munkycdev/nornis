using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record ArtifactListQuery(
    Guid CampaignId,
    Guid ActingUserId,
    CampaignRole ActingUserRole,
    ArtifactType? Type = null,
    ArtifactStatus? Status = null);
