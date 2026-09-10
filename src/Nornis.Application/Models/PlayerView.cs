using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A player as the Party page shows them: the resolved display name, and the membership they
/// are linked to, if any. Players carry no visibility — every member sees every player, as
/// every member sees every member.
/// </summary>
/// <param name="WorldMemberId">Null means not on Nornis. Nothing else.</param>
/// <param name="Role">The linked member's role; null for an unlinked player.</param>
public record PlayerView(
    Guid Id,
    Guid WorldId,
    string Name,
    Guid? WorldMemberId,
    WorldRole? Role);
