namespace Nornis.Domain.Entities;

/// <summary>
/// A capability URL that opens a world's party shelf for one <see cref="Player"/> who is not
/// on Nornis — Henry's kids, who play every week and cannot agree to terms of service. The GM
/// mints it on the Party page, hands it to that one person, and can take it back from that
/// one person. It opens the party shelf and nothing else, and it is closed distribution, not
/// publication: unlisted, uncached, never linked from the public world.
///
/// A link stands in for an account and is only minted for a player without one. When the
/// player is claimed or linked into a member the unlinked row is deleted, and the link goes
/// with it by cascade; nothing else has to remember to retire it.
/// </summary>
public class ShelfLink
{
    public Guid Id { get; set; }

    public Guid PlayerId { get; set; }

    /// <summary>URL-safe capability secret — never logged, never in a non-GM response.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a GM revoked the link; <c>null</c> means it is the player's standing link.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>When the link was last opened; <c>null</c> means never.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    // Navigation properties
    public Player Player { get; set; } = null!;

    public User CreatedByUser { get; set; } = null!;

    /// <summary>Whether the link opens. There is no expiry: revocation is the one control.</summary>
    public bool IsActive => RevokedAt is null;
}
