using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class Source
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    /// <summary>
    /// The campaign this source's events happened in, if any. Nullable on purpose:
    /// worldbuilding lore, GM prep, and setting documents belong to no campaign.
    /// </summary>
    public Guid? CampaignId { get; set; }

    public SourceType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string? Uri { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public VisibilityScope Visibility { get; set; }

    public SourceProcessingStatus ProcessingStatus { get; set; }

    /// <summary>
    /// When <see cref="ProcessingStatus"/> last changed. Null for rows that predate the
    /// column and have not moved since.
    ///
    /// Exists for one reason: a source can wedge at Queued when its extraction message
    /// dead-letters, and every route out of that needs to know how long it has been stuck.
    /// Nothing sets this by hand — the DbContext stamps it whenever the status column is
    /// actually modified, because a clock a safety gate depends on cannot rely on
    /// thirty-eight call sites remembering it.
    /// </summary>
    public DateTimeOffset? StatusChangedAt { get; set; }

    /// <summary>
    /// Whether processing this source runs AI extraction. When false the source is
    /// stored as part of the record without generating proposals — reference documents,
    /// flavor writing, and other material that shouldn't touch canon.
    /// </summary>
    public bool ExtractionEnabled { get; set; } = true;

    /// <summary>
    /// Machine-derived text from attachments — PDF text, file contents, vision reads.
    /// Persisted before extraction so a redelivered message never re-buys it; cleared
    /// when attachments change. Kept separate from <see cref="Body"/> so the user's
    /// typed notes stay theirs.
    /// </summary>
    public string? DerivedText { get; set; }

    /// <summary>
    /// The GM's own words to the party when they made a reveal, kept apart from
    /// <see cref="Body"/>. The body composes those words together with a list of what was
    /// promoted, and the player-facing view needs the note without that list — recovering it by
    /// splitting the body would make the composition format and a parsing format two copies of
    /// one rule. Null on every source that is not a reveal, and on reveals made before this
    /// existed.
    /// </summary>
    public string? RevealNote { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public Campaign? Campaign { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public ICollection<SourceExtraction> SourceExtractions { get; set; } = [];
}
