namespace Nornis.Domain.Entities;

/// <summary>
/// A person who plays in a world. Linked to a <see cref="WorldMember"/> when that person has
/// an account and has joined; unlinked when the table knows them only by name — Henry, who
/// plays every week and has chosen not to use Nornis. Characters belong to players, never to
/// memberships directly, so Henry's character can exist before Henry ever signs in.
///
/// A player confers no rights. Membership governs every read and write; the rights a
/// character's steward holds come from the member its player is linked to, or fall to the GM
/// while there is none (see <c>CharacterService.IsSteward</c>).
/// </summary>
public class Player
{
    public const int MaxNameChars = 200;

    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>
    /// The membership this player is the same person as, or null: not on Nornis. Nothing else.
    /// There is no "invited" or "pending" state — an invite that is redeemed creates a member
    /// and its own player, and the old one is claimed into it.
    /// </summary>
    public Guid? WorldMemberId { get; set; }

    /// <summary>
    /// The name the GM gave an unlinked player. Written for a linked player too — the member's
    /// name at the time — but unread while the link stands: <c>PlayerDisplayName</c> reads the
    /// membership instead, so a member's display name has one home.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public WorldMember? WorldMember { get; set; }

    public ICollection<Character> Characters { get; set; } = [];
}
