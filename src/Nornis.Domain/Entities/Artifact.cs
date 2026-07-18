using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class Artifact
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public ArtifactType Type { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Summary { get; set; }

    public VisibilityScope Visibility { get; set; }

    public decimal? Confidence { get; set; }

    public ArtifactStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// The user whose source created this artifact (owner for Private visibility).
    /// Null for unattributable legacy rows — Private + null owner is GM-only.
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public World World { get; set; } = null!;

    public User? CreatedByUser { get; set; }

    public ICollection<ArtifactFact> ArtifactFacts { get; set; } = [];
}
