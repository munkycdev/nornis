using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record AddMemberCommand(
    Guid CampaignId,
    Guid TargetUserId,
    CampaignRole Role,
    Guid ActingUserId);
