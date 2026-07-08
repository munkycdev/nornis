namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// The AI continuity assessment for a campaign. <c>Score</c> is the blended snapshot stored when
/// the assessment ran; <c>EffectiveScore</c> is recomputed from currently-Open findings. When the
/// campaign has never been assessed, <c>HasData</c> is false and the scores/findings are empty.
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
    Guid? ArtifactId,
    string Status);
