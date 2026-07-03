using Nornis.Domain.Enums;

namespace Nornis.Application.Authorization;

public static class CampaignRoleExtensions
{
    public static int Rank(this CampaignRole role) => role switch
    {
        CampaignRole.GM => 3,
        CampaignRole.Player => 2,
        CampaignRole.Observer => 1,
        _ => 0
    };

    public static bool IsAtLeast(this CampaignRole role, CampaignRole required)
        => role.Rank() >= required.Rank();
}
