using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The one place a membership is built, because a membership is two rows: the
/// <see cref="WorldMember"/> and the <see cref="Player"/> that is the same person at the table.
/// Every member has exactly one linked player from the moment the membership exists, and the
/// Party page, the steward rule and claiming all rest on that. The member carries its player
/// as a navigation, so persisting the member persists both — a call site cannot forget the
/// second row without discarding it on purpose.
///
/// <c>MemberPlayerScanTests</c> asserts no other <c>new WorldMember</c> exists in the tree.
/// </summary>
public static class WorldMembership
{
    public static WorldMember Create(Guid worldId, Guid userId, WorldRole role, DateTimeOffset now, string? displayName = null)
    {
        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            Role = role,
            DisplayName = displayName,
            JoinedAt = now,
        };

        member.Player = new Player
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            WorldMemberId = member.Id,
            // The member's name at this moment: unread while the link stands, and what the
            // player is called if the member ever leaves. Refreshed at removal.
            Name = MemberDisplayName.For(member),
            CreatedAt = now,
            UpdatedAt = now,
        };

        return member;
    }

    /// <summary>
    /// The same membership seen as another role — the GM's "view as player". A copy, not a
    /// membership: it is never persisted and carries no player.
    /// </summary>
    public static WorldMember AsRole(WorldMember member, WorldRole role) => new()
    {
        Id = member.Id,
        WorldId = member.WorldId,
        UserId = member.UserId,
        Role = role,
        DisplayName = member.DisplayName,
        JoinedAt = member.JoinedAt,
        LearnedSeenAt = member.LearnedSeenAt,
    };
}
