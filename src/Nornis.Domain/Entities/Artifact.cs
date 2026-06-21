using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class Artifact
{
    public Guid Id { get; set; }

    public Guid CampaignId { get; set; }

    public ArtifactType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public VisibilityScope Visibility { get; set; }

    public decimal? Confidence { get; set; }

    public ArtifactStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public Campaign Campaign { get; set; } = null!;

    public ICollection<ArtifactFact> ArtifactFacts { get; set; } = [];
}
