using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class Source
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public SourceType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string? Uri { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedByUserId { get; set; }

    public VisibilityScope Visibility { get; set; }

    public SourceProcessingStatus ProcessingStatus { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public User CreatedByUser { get; set; } = null!;

    public ICollection<SourceExtraction> SourceExtractions { get; set; } = [];
}
