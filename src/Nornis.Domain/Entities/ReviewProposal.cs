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

    /// <summary>Accepted by <paramref name="reviewerId"/> at <paramref name="at"/>.</summary>
    public void Accept(Guid reviewerId, DateTimeOffset at) =>
        Decide(ReviewProposalStatus.Accepted, reviewerId, at);

    /// <summary>Rejected by <paramref name="reviewerId"/> at <paramref name="at"/>.</summary>
    public void Reject(Guid reviewerId, DateTimeOffset at) =>
        Decide(ReviewProposalStatus.Rejected, reviewerId, at);

    /// <summary>
    /// Marks an edit by <paramref name="reviewerId"/>. Named for what it records rather than
    /// what it does — the new <see cref="ProposedValueJson"/> is set by the caller, and this
    /// only stamps who changed it and when.
    /// </summary>
    public void MarkEdited(Guid reviewerId, DateTimeOffset at) =>
        Decide(ReviewProposalStatus.Edited, reviewerId, at);

    /// <summary>
    /// The review-provenance invariant, in one place. Status, reviewer and timestamp are one
    /// fact — a resolved proposal with no reviewer is a proposal that decided itself — and
    /// they were assembled by hand at eight call sites across seven services, which is eight
    /// chances to write two fields of the three.
    /// <para>
    /// Private, and reachable only through the three outcomes above, so no caller can stamp a
    /// review onto a status that is not a decision. Time comes in as a parameter, as it does
    /// on <see cref="WorldInvite.CanBeRedeemed"/>: an entity that reads the clock is an entity
    /// that cannot be tested at a chosen moment.
    /// </para>
    /// </summary>
    private void Decide(ReviewProposalStatus outcome, Guid reviewerId, DateTimeOffset at)
    {
        Status = outcome;
        ReviewedAt = at;
        ReviewedByUserId = reviewerId;
    }

    // Navigation properties
    public ReviewBatch ReviewBatch { get; set; } = null!;
}
