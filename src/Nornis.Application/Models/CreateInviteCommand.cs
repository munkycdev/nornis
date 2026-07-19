using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A GM's request to mint a reusable invite link for a world. <paramref name="ExpiresAt"/>
/// and <paramref name="MaxUses"/> are optional caps (null = never expires / unlimited).
/// </summary>
public record CreateInviteCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole Role,
    DateTimeOffset? ExpiresAt = null,
    int? MaxUses = null);
