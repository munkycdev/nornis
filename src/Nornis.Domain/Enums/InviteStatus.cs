namespace Nornis.Domain.Enums;

/// <summary>
/// The redeemability of a <see cref="Entities.WorldInvite"/> at a given moment.
/// Only <see cref="Active"/> invites can be accepted.
/// </summary>
public enum InviteStatus
{
    Active,
    Revoked,
    Expired,
    Exhausted
}
