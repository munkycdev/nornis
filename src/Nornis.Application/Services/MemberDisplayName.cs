using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

/// <summary>
/// How a world member is named in world-facing material.
///
/// The fallback is a truncated id rather than the account username on purpose: artifact
/// detail carries these names and is served anonymously on the public world page, so a
/// username here would leave the world with whoever read it.
///
/// <see cref="CostService"/> names members differently — falling back to the username — and
/// is right to: the cost dashboard is GM-only and never served publicly. The two are not a
/// duplicated rule, they are two rules for two audiences.
/// </summary>
public static class MemberDisplayName
{
    public static string For(WorldMember member) =>
        !string.IsNullOrWhiteSpace(member.DisplayName)
            ? member.DisplayName!
            : $"User {member.UserId.ToString()[..8]}";
}
