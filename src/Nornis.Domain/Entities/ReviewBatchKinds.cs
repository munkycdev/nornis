namespace Nornis.Domain.Entities;

/// <summary>
/// The named values of <see cref="ReviewBatch.Kind"/>. Null is not listed because null is
/// not a kind: it marks a source's own extraction batch, and the filtered unique index
/// that enforces one-extraction-batch-per-source keys on exactly that. Every synthetic
/// batch names its producer here — these strings are persisted, serve as per-source
/// idempotency keys for sweeps, and one of them ("RelationshipBackfill") is mirrored in
/// the Web's ReviewQueuePanel, which sits across a deploy boundary and cannot reference
/// this class.
/// </summary>
public static class ReviewBatchKinds
{
    public const string SessionWrapUp = "SessionWrapUp";
    public const string ArtifactMerge = "ArtifactMerge";
    public const string Reveal = "Reveal";
    public const string ContinuityFix = "ContinuityFix";
    public const string StorylineRetrospective = "StorylineRetrospective";
    public const string RelationshipBackfill = "RelationshipBackfill";
    public const string SummaryRefresh = "SummaryRefresh";
}
