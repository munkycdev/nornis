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

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public World World { get; set; } = null!;

    public Campaign? Campaign { get; set; }

    public LibraryDocument? LastPrintedDocument { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public ICollection<AdventureSection> Sections { get; set; } = [];
}
