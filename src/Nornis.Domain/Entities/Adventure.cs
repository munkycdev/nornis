using Nornis.Domain.Models;

namespace Nornis.Domain.Entities;

/// <summary>
/// A GM's prep for one adventure: a pitch and an ordered list of
/// <see cref="AdventureSection"/>s, written by hand or drafted with the AI, then printed as a
/// booklet or exported into the world as a source.
///
/// Always GM-only, and deliberately without a <c>Visibility</c> column: a column that could
/// hold PartyVisible would be a guard that must never fire. Prep is planning, not canon. It
/// reaches canon only through an export, which is an ordinary source and goes through review.
/// </summary>
public class Adventure : ISlugged
{
    /// <summary>The longest table brief (feature 37): a paragraph, not a document.</summary>
    public const int MaxTableBriefChars = 2_000;

    /// <summary>The longest design draft (feature 37): well under what one prompt can carry.</summary>
    public const int MaxDraftChars = 60_000;

    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>The campaign this prep is for. Null means world-level prep, attached to no campaign.</summary>
    public Guid? CampaignId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Per-world URL slug; see <see cref="ISlugged"/>.</summary>
    public string? Slug { get; set; }

    string ISlugged.SlugSource => Title;

    /// <summary>The GM's premise. Null or empty means no pitch: drafting the Overview then needs an instruction.</summary>
    public string? Pitch { get; set; }

    /// <summary>
    /// The newest booklet filed to the Library from this Adventure. Null means never printed,
    /// or the print was since deleted from the Library.
    /// </summary>
    public Guid? LastPrintedDocumentId { get; set; }

    /// <summary>
    /// The GM's bibliographic details (feature 32). Never null: an Adventure with none set has
    /// an empty instance. Each Adventure owns its own instance, so never share one between two.
    /// </summary>
    public AdventureDetails Details { get; set; } = new();

    /// <summary>
    /// The design draft (feature 37): one Markdown document the GM and the Loremaster iterate on
    /// before it is split into sections. Null means no draft. It lives outside
    /// <see cref="Sections"/> on purpose, so no booklet, export or outline count can ever read it.
    /// </summary>
    public string? DraftMarkdown { get; set; }

    /// <summary>When the draft last changed; a write names the stamp it was based on. Null while there is no draft.</summary>
    public DateTimeOffset? DraftUpdatedAt { get; set; }

    /// <summary>
    /// Who is at the table (feature 37): the party, their level, the runtime, players' quirks.
    /// Every Prep prompt carries it. Null or empty means none.
    /// </summary>
    public string? TableBrief { get; set; }

    /// <summary>
    /// What the Adventure records about AI's part in it (feature 38). Never null; each Adventure
    /// owns its own instance. Only the repository's AI writes set its recorded flags.
    /// </summary>
    public AdventureAiUse AiUse { get; set; } = new();

    /// <summary>The cover art (feature 38). Null means none: the booklet's cover is typeset only.</summary>
    public AdventureCover? Cover { get; set; }

    /// <summary>How many cover images have been generated for this Adventure, chosen or not (feature 38's cap).</summary>
    public int CoverImagesGenerated { get; set; }

    /// <summary>The art brief the GM is working on (feature 38), kept so a reload doesn't lose it.</summary>
    public string? CoverBrief { get; set; }

    /// <summary>
    /// The generated cover options, oldest first (feature 38). Each is a PNG at
    /// <c>CoverBlobs.OptionPath</c>; the chosen cover is one of them. They stay until the
    /// Adventure is deleted, and the image cap bounds how many there can be.
    /// </summary>
    public List<Guid> CoverOptionIds { get; set; } = [];

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public World World { get; set; } = null!;

    public Campaign? Campaign { get; set; }

    public LibraryDocument? LastPrintedDocument { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public ICollection<AdventureSection> Sections { get; set; } = [];

    /// <summary>The books Prep reads for this Adventure (feature 37); none means every book the GM has.</summary>
    public ICollection<AdventureSourceBook> SourceBooks { get; set; } = [];

    /// <summary>The maps and handouts printed at the back of the booklet (feature 38), in their order.</summary>
    public ICollection<AdventureHandout> Handouts { get; set; } = [];
}
