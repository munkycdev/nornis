namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// The AI continuity assessment for a world. <c>Score</c> is the blended snapshot stored when
/// the assessment ran; <c>EffectiveScore</c> is recomputed from findings that are currently
/// Open and not stale. When the world has never been assessed, <c>HasData</c> is false and the
/// scores/findings are empty.
/// </summary>
public record ContinuityAssessmentResponse(
    bool HasData,
    Guid? AssessmentId,
    DateTimeOffset? CreatedAt,
    string? Model,
    int Score,
    int EffectiveScore,
    int HeuristicScore,
    IReadOnlyList<ContinuityFindingResponse> Findings);

public record ContinuityFindingResponse(
    Guid Id,
    string Category,
    string Severity,
    string Summary,
    string? SuggestedAction,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<ContinuityEvidenceItemResponse> EvidenceItems,
    Guid? ArtifactId,
    string Status,
    bool IsStale);

/// <summary>
/// A cited evidence ref resolved for display: kind, label, and the artifact to open. Changed
/// items were edited after the assessment ran; missing ones no longer exist.
/// </summary>
public record ContinuityEvidenceItemResponse(
    string RefId,
    string Kind,
    string Label,
    Guid? ArtifactId,
    bool ChangedSinceAudit,
    bool Missing);

/// <summary>
/// Result of drafting a fix for a finding. <c>ProposalCount</c> 0 means the fixer had nothing
/// concrete to propose and no batch was created.
/// </summary>
public record DraftFixResponse(
    Guid? BatchId,
    Guid? SourceId,
    int ProposalCount);
