using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A GM's request to mint a reusable invite link for a world. <paramref name="ExpiresAt"/>
/// and <paramref name="MaxUses"/> are optional caps (null = never expires / unlimited).
/// </summary>
public record CreateInviteCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole InvitedRole,
    DateTimeOffset? ExpiresAt = null,
    int? MaxUses = null,
    // Last, and named differently from InvitedRole on purpose: two adjacent WorldRole
    // parameters in an authorization command is a swap the compiler cannot catch.
    WorldRole ActingUserRole = WorldRole.Observer);
