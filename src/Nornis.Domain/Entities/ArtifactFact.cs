using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class ArtifactFact
{
    public Guid Id { get; set; }

    public Guid ArtifactId { get; set; }

    public string Predicate { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public decimal? Confidence { get; set; }

    public TruthState TruthState { get; set; }

    public VisibilityScope Visibility { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public Artifact Artifact { get; set; } = null!;
}
