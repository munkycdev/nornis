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

    // Navigation properties
    public World World { get; set; } = null!;

    public Campaign? Campaign { get; set; }

    public User CreatedByUser { get; set; } = null!;

    public ICollection<SourceExtraction> SourceExtractions { get; set; } = [];
}
