using Nornis.Domain.Enums;

namespace Nornis.Application.Authorization;

public static class WorldRoleExtensions
{
    public static int Rank(this WorldRole role) => role switch
    {
        WorldRole.GM => 3,
        WorldRole.Player => 2,
        WorldRole.Observer => 1,
        _ => 0
    };

    public static bool IsAtLeast(this WorldRole role, WorldRole required)
        => role.Rank() >= required.Rank();
}
