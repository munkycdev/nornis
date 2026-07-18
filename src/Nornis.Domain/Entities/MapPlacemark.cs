namespace Nornis.Domain.Entities;

/// <summary>
/// A pin on a map image: links a MapImage attachment to a Location artifact at a
/// normalized position. Created only by the proposal applicator when a map proposal
/// is accepted. ArtifactId is a loose reference (SourceReference pattern — a hard FK
/// would create multiple-cascade-path conflicts): placemarks are cleaned up
/// explicitly where artifacts are hard-deleted and filtered defensively at read time.
/// </summary>
public class MapPlacemark
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid SourceAttachmentId { get; set; }

    public Guid ArtifactId { get; set; }

    /// <summary>Normalized 0..1 from the image's top-left corner.</summary>
    public decimal X { get; set; }

    /// <summary>Normalized 0..1 from the image's top-left corner.</summary>
    public decimal Y { get; set; }

    /// <summary>The name as written on the map (may differ from the artifact name).</summary>
    public string? Label { get; set; }

    public decimal? Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    // Navigation properties
    public SourceAttachment Attachment { get; set; } = null!;
}
