namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// What the gauge observed about a candidate, and what it normalized to. Both halves cross the
/// wire: the counts are what the page renders a phrase from, and the normalized values are what
/// the score was built from, so a lopsided candidate can be shown as lopsided. Nothing here is
/// recomputed client-side.
/// </summary>
public record ConvergenceComponentsResponse(
    int DaysHidden,
    int PartyVisibleFactsOnAnchor,
    int MissingArtifactCount,
    bool IsSelfContained,
    string? StorylineStatus,
    string? ContradictionSeverity,
    bool ContradictionAssessed,
    double Dormancy,
    double AnchorFamiliarity,
    double SelfContainment,
    double StorylineState,
    double? ContradictionPressure);

/// <summary>
/// <paramref name="MissingArtifactIds"/> is the closure a reveal would require, carried so the
/// page can open the reveal flow without working it out again.
/// </summary>
public record ConvergenceCandidateResponse(
    string Kind,
    Guid Id,
    Guid AnchorArtifactId,
    string AnchorName,
    string Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<Guid> MissingArtifactIds,
    ConvergenceComponentsResponse Components,
    int Score);

/// <summary>
/// <paramref name="AssessmentId"/> is null when the world has never been assessed — the same
/// distinction each candidate's <c>ContradictionAssessed</c> carries, at the top level.
/// <paramref name="TotalCandidates"/> may exceed the returned list, which is capped.
/// </summary>
public record ConvergenceResponse(
    Guid WorldId,
    DateTimeOffset GeneratedAt,
    Guid? AssessmentId,
    int TotalCandidates,
    IReadOnlyList<ConvergenceCandidateResponse> Candidates);
