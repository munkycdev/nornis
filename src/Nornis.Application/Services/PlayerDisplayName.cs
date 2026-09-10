using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

/// <summary>
/// How a player is named on screen. A linked player is the member, and reads through
/// <see cref="MemberDisplayName"/> so a member's name has one home and one public-safe
/// fallback; an unlinked player is the name the GM gave them. Neither is ever a username.
/// </summary>
public static class PlayerDisplayName
{
    public static string For(Player player, WorldMember? member) =>
        member is not null ? MemberDisplayName.For(member) : player.Name;

    /// <summary>The same rule over a world's members, for callers naming many players at once.</summary>
    public static string For(Player player, IReadOnlyList<WorldMember> members) =>
        For(player, player.WorldMemberId is { } memberId ? members.FirstOrDefault(m => m.Id == memberId) : null);
}
