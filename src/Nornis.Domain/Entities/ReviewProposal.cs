using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

public class ReviewProposal
{
    public Guid Id { get; set; }

    public Guid ReviewBatchId { get; set; }

    public ReviewChangeType ChangeType { get; set; }

    public ReviewTargetType TargetType { get; set; }

    public Guid? TargetId { get; set; }

    public string ProposedValueJson { get; set; } = string.Empty;

    public string? Rationale { get; set; }

    public decimal? Confidence { get; set; }

    public ReviewProposalStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public ReviewBatch ReviewBatch { get; set; } = null!;
}
