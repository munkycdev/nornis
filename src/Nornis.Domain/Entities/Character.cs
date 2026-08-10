namespace Nornis.Domain.Entities;

/// <summary>
/// A playable identity owned by a world member. A member may have any number of
/// characters, and a character may participate in any number of campaigns. Distinct
/// from the AI-extracted Artifact of type Character.
/// </summary>
public class Character
{
    /// <summary>
    /// Ceiling on <see cref="Sheet"/>. Generous for text, short of a document. Over-length
    /// input is refused rather than truncated: silently trimming a field whose whole purpose
    /// is being the player's own record is data loss.
    /// </summary>
    public const int MaxSheetChars = 20_000;

    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid WorldMemberId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Optional link to the AI-extracted Artifact (Type == Character) describing the
    /// same fictional person.
    /// </summary>
    public Guid? ArtifactId { get; set; }

    /// <summary>
    /// The player's own sheet: free-form text they maintain, for tables whose real sheet is on
    /// paper.
    ///
    /// Uninterpreted, and it must stay that way. Nothing parses it, validates it against a game
    /// system, or computes a value from it — a field Nornis understands the units of is a game
    /// system Nornis has undertaken to support, in every system, forever.
    ///
    /// **It must never reach an AI path.** No prompt assembly, context builder, digest, recap,
    /// or embedding input may read it. Everything the Loremaster can cite carries provenance
    /// back to a source; this carries none, so quoting it would let unprovenanced text be
    /// answered as though it were canon. <c>CharacterSheetIsolationTests</c> enforces this by
    /// scanning source, because no compiler can.
    /// </summary>
    public string? Sheet { get; set; }

    /// <summary>
    /// False — the default — means the sheet is readable by its owner and by GMs only. True
    /// shares it with every member of the world.
    ///
    /// A bool rather than a <c>VisibilityScope</c> on purpose: the enum has three states and
    /// only two are meaningful for player-authored text, so Private and GMOnly would both have
    /// to mean "owner and GM" — one value meaning two things, which is the sentinel defect.
    /// </summary>
    public bool SheetSharedWithParty { get; set; }

    public DateTimeOffset? SheetUpdatedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public WorldMember WorldMember { get; set; } = null!;

    public Artifact? Artifact { get; set; }

    public ICollection<CampaignCharacter> CampaignCharacters { get; set; } = [];
}
