using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class SourceExtraction
{
    public Guid Id { get; set; }

    public Guid SourceId { get; set; }

    public SourceExtractionType ExtractionType { get; set; }

    public string Text { get; set; } = string.Empty;

    public decimal? Confidence { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
