using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class ArtifactRelationship
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public Guid ArtifactAId { get; set; }

    public Guid ArtifactBId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Description { get; set; }

    public decimal? Confidence { get; set; }

    public TruthState TruthState { get; set; }

    public VisibilityScope Visibility { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public Campaign Campaign { get; set; } = null!;

    public Artifact ArtifactA { get; set; } = null!;

    public Artifact ArtifactB { get; set; } = null!;
}
