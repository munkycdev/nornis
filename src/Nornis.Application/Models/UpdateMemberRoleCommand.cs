using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record UpdateMemberRoleCommand(
    Guid CampaignId,
    Guid TargetUserId,
    CampaignRole NewRole,
    Guid ActingUserId);
