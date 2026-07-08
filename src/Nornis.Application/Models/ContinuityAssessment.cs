namespace Nornis.Application.Models;

/// <summary>
/// The AI continuity assessment surfaced to the API/UI. <see cref="Score"/> is the blended score
/// snapshot stored when the assessment ran; <see cref="EffectiveScore"/> is recomputed from the
/// findings that are currently Open, so dismissing a finding raises the effective score.
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

/// <summary>A single continuity finding as presented to callers.</summary>
public record ContinuityFindingView(
    Guid Id,
    string Category,
    string Severity,
    string Summary,
    string? SuggestedAction,
    IReadOnlyList<string> Evidence,
    Guid? ArtifactId,
    string Status);
