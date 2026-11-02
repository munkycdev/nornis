using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// One part of an <see cref="Adventure"/>, in Markdown. Sections are flat and ordered by
/// <see cref="SortOrder"/>; a chapter is a section <see cref="Kind"/>, not a parent row, so
/// reordering is one column. The kind tells drafting what the section is for and the print
/// view how to lay it out. The GM keeps the shape.
/// </summary>
public class AdventureSection
{
    public Guid Id { get; set; }

    public Guid AdventureId { get; set; }

    public AdventureSectionKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The section's Markdown. Empty means unfilled: Draft is offered and Revise is not.</summary>
    public string BodyMarkdown { get; set; } = string.Empty;

    /// <summary>1-based position within the Adventure.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Bumped on every write, and the concurrency token for them: a save or a draft names the
    /// value it was based on, and a write based on an older one is refused rather than
    /// overwriting what changed in between.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public Adventure Adventure { get; set; } = null!;

    public ICollection<AdventureSectionRevision> Revisions { get; set; } = [];
}
