using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class SourceReference
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public SourceReferenceTargetType TargetType { get; set; }

    public Guid TargetId { get; set; }

    public string? Quote { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Navigation properties
    public Source Source { get; set; } = null!;
}
