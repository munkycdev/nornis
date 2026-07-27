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

    /// <summary>
    /// For an accepted CreateArtifact: true when apply-time dedup bound it to an artifact
    /// that already existed instead of inserting a new one. Null on every row written
    /// before the flag existed, and on every non-create proposal.
    ///
    /// Load-bearing for reprocess: <c>SourceReprocessService</c> would otherwise read this
    /// proposal's TargetId as "this source created that artifact" and hard-delete canon
    /// that predates the source.
    /// </summary>
    public bool? AppliedToExistingArtifact { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReviewedAt { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public byte[] RowVersion { get; set; } = [];

    // Navigation properties
    public ReviewBatch ReviewBatch { get; set; } = null!;
}
