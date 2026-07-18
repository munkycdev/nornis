using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class ArtifactRelationship
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid ArtifactAId { get; set; }

    public Guid ArtifactBId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal? Confidence { get; set; }

    public TruthState TruthState { get; set; }

    public VisibilityScope Visibility { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The user whose source created this relationship (owner for Private visibility).
    /// Null for unattributable legacy rows and pre-existing GM PartOf links — Private +
    /// null owner is GM-only.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public World World { get; set; } = null!;

    public User? CreatedByUser { get; set; }

    public Artifact ArtifactA { get; set; } = null!;

    public Artifact ArtifactB { get; set; } = null!;
}
