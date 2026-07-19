using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A reusable invitation link to a world. A GM mints one, shares the resulting
/// <c>nornis.app/invite/{Code}</c> URL, and anyone who opens it and authenticates joins
/// the world with <see cref="Role"/>. Reusable (not single-use) but bounded by optional
/// expiry and use-count limits, and revocable at any time.
/// </summary>
public class WorldInvite
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>URL-safe redemption token. Treated as a capability secret — never logged.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Role granted to whoever redeems this invite.</summary>
    public WorldRole Role { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the invite stops working; <c>null</c> means it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Maximum successful redemptions; <c>null</c> means unlimited.</summary>
    public int? MaxUses { get; set; }

    /// <summary>Successful redemptions so far.</summary>
    public int UseCount { get; set; }

    /// <summary>When a GM revoked the invite; <c>null</c> means still active.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token (matches <see cref="World"/>/<see cref="User"/>). Guards
    /// against two simultaneous redemptions both slipping past the last <see cref="MaxUses"/> slot.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public World World { get; set; } = null!;

    public User CreatedByUser { get; set; } = null!;

    /// <summary>
    /// The invite's redeemability at <paramref name="now"/>. Pure — the caller supplies the
    /// clock so this stays trivially unit-testable. Revocation wins over expiry, which wins
    /// over exhaustion.
    /// </summary>
    public InviteStatus StatusAt(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return InviteStatus.Revoked;
        }

        if (ExpiresAt is { } expiry && expiry <= now)
        {
            return InviteStatus.Expired;
        }

        if (MaxUses is { } max && UseCount >= max)
        {
            return InviteStatus.Exhausted;
        }

        return InviteStatus.Active;
    }

    /// <summary>Whether this invite can be redeemed at <paramref name="now"/>.</summary>
    public bool CanBeRedeemed(DateTimeOffset now) => StatusAt(now) == InviteStatus.Active;
}
