namespace Nornis.Application.Models;

/// <summary>
/// The AI continuity assessment surfaced to the API/UI. <see cref="Score"/> is the blended score
/// snapshot stored when the assessment ran; <see cref="EffectiveScore"/> is recomputed from the
/// findings that are currently Open and not stale, so dismissing a finding — or editing the
/// evidence it cites — raises the effective score.
/// </summary>
public record ContinuityAssessment(
    bool HasData,
    Guid? AssessmentId,
    DateTimeOffset? CreatedAt,
    string? Model,
    int Score,
    int EffectiveScore,
    int HeuristicScore,
    IReadOnlyList<ContinuityFindingView> Findings);

/// <summary>
/// A single continuity finding as presented to callers. <see cref="IsStale"/> is derived, never
/// stored: true when any cited evidence item changed after the assessment ran or no longer
/// exists — the finding's grounding is out of date, so it stops counting toward the effective
/// score until a re-run re-establishes it.
/// </summary>
public record ContinuityFindingView(
    Guid Id,
    string Category,
    string Severity,
    string Summary,
    string? SuggestedAction,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<ContinuityEvidenceItemView> EvidenceItems,
    Guid? ArtifactId,
    string Status,
    bool IsStale);

/// <summary>
/// One cited evidence ref resolved for display: what kind of item it is, a human-readable
/// label, and the artifact a GM should open to act on it. <see cref="ChangedSinceAudit"/> is
/// true when the item was edited after the assessment ran; <see cref="Missing"/> when the ref
/// no longer resolves to a live item (deleted or merged away).
/// </summary>
public record ContinuityEvidenceItemView(
    string RefId,
    string Kind,
    string Label,
    Guid? ArtifactId,
    bool ChangedSinceAudit,
    bool Missing);
