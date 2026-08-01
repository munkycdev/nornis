using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Api.Extensions;

public static class HttpContextExtensions
{
    /// <summary>
    /// Request header a GM's client sends to see the world as a player ("View as player").
    /// Honored only when the authenticated member's real role is GM; the value must be
    /// "Player". Any other sender or value is ignored — the header can only ever downgrade.
    /// </summary>
    public const string ViewAsHeaderName = "X-Nornis-View-As";

    public static User GetNornisUser(this HttpContext context)
    {
        return context.Items["NornisUser"] as User
            ?? throw new InvalidOperationException("NornisUser not found in HttpContext. Ensure UserProvisioningMiddleware is in the pipeline.");
    }

    public static WorldMember GetWorldMember(this HttpContext context)
    {
        return context.Items["WorldMember"] as WorldMember
            ?? throw new InvalidOperationException("WorldMember not found in HttpContext. Ensure WorldMemberActionFilter is applied.");
    }

    /// <summary>
    /// Applies the view-as-player downgrade at membership-resolution time, so every
    /// downstream consumer — read services and GM write-gates alike — sees a Player member.
    /// GM-only endpoints therefore fail closed (403) while the view is active, which is the
    /// intended semantics: in player view you <em>are</em> a player for the whole request.
    /// Returns a detached copy rather than mutating the (possibly change-tracked) entity.
    /// </summary>
    public static WorldMember ApplyViewAs(this HttpContext context, WorldMember member)
    {
        if (member.Role != WorldRole.GM
            || !context.Request.Headers.TryGetValue(ViewAsHeaderName, out var values)
            || !string.Equals(values.ToString(), "Player", StringComparison.OrdinalIgnoreCase))
        {
            return member;
        }

        return new WorldMember
        {
            Id = member.Id,
            WorldId = member.WorldId,
            UserId = member.UserId,
            Role = WorldRole.Player,
            DisplayName = member.DisplayName,
            JoinedAt = member.JoinedAt,
        };
    }
}
