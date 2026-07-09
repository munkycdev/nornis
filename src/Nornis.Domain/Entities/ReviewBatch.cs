using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class ReviewBatch
{
    public Guid Id { get; set; }

    public Guid WorldId { get; set; }

    public Guid SourceId { get; set; }

    public ReviewBatchStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    // Navigation properties
    public World World { get; set; } = null!;

    public Source Source { get; set; } = null!;

    public ICollection<ReviewProposal> ReviewProposals { get; set; } = [];
}
