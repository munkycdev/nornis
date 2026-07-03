using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public record CampaignWithRoleDto(
    Campaign Campaign,
    CampaignRole Role);
