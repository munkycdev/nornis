using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

public enum ConvergenceCandidateKind
{
    Artifact,
    Fact,
    Relationship
}

/// <summary>
/// What the gauge observed about one candidate, and what those observations normalize to.
///
/// Both halves are carried on purpose. The raw counts are what a caller renders a phrase from
/// ("hidden for 94 days", "reveals cleanly on its own") without recomputing anything, and the
/// normalized values are what the total was built from — so a lopsided score can be shown as
/// lopsided rather than averaged into a number that explains nothing.
/// </summary>
public sealed record ConvergenceComponents
{
    // ---------------------------------------------------------------- observations --

    public required int DaysHidden { get; init; }
    public required int PartyVisibleFactsOnAnchor { get; init; }

    /// <summary>Artifacts that would have to be revealed alongside this candidate. Zero is the
    /// self-contained case.</summary>
    public required int MissingArtifactCount { get; init; }

    /// <summary>Status of the most-finished storyline the anchor takes part in; null when it
    /// takes part in none.</summary>
    public required ArtifactStatus? StorylineStatus { get; init; }

    /// <summary>Severity of the contradiction citing this candidate; null when none cites it.
    /// Meaningless unless <see cref="ContradictionAssessed"/>.</summary>
    public required ContinuityFindingSeverity? ContradictionSeverity { get; init; }

    /// <summary>
    /// False when the world has no health assessment to read. Distinct from a null severity,
    /// which means an assessment existed and cited nothing — "we did not look" and "we looked
    /// and found nothing" must not render the same way.
    /// </summary>
    public required bool ContradictionAssessed { get; init; }

    // ----------------------------------------------------------------- normalized --

    public required double Dormancy { get; init; }
    public required double AnchorFamiliarity { get; init; }
    public required double SelfContainment { get; init; }
    public required double StorylineState { get; init; }

    /// <summary>Null when no assessment was available to read.</summary>
    public required double? ContradictionPressure { get; init; }

    public bool IsSelfContained => MissingArtifactCount == 0;
}

/// <summary>
/// One piece of hidden material, scored. <see cref="MissingArtifactIds"/> is the closure the
/// reveal would require, so a caller can hand the candidate and its dependencies to
/// <c>IRevealService.RevealAsync</c> without working it out again.
/// </summary>
public sealed record ConvergenceCandidate
{
    public required ConvergenceCandidateKind Kind { get; init; }

    /// <summary>The fact, relationship, or artifact itself.</summary>
    public required Guid Id { get; init; }

    /// <summary>The artifact this hangs on — itself, when the candidate is an artifact.</summary>
    public required Guid AnchorArtifactId { get; init; }

    public required string AnchorName { get; init; }

    /// <summary>What the candidate says: a fact's predicate and value, a relationship's type,
    /// an artifact's name.</summary>
    public required string Description { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<Guid> MissingArtifactIds { get; init; }

    public required ConvergenceComponents Components { get; init; }

    /// <summary>0–100, matching the continuity score's convention.</summary>
    public required int Score { get; init; }

    /// <summary>
    /// The why-now, when a GM has asked for one. Null is the ordinary state: the ranking stands
    /// on its components, and this only ever decorates it.
    /// </summary>
    public string? Rationale { get; init; }
}

/// <summary>
/// A world's hidden material, ranked. <see cref="AssessmentId"/> names the health assessment
/// the contradiction signal was read from, or null when the world has none — the same
/// distinction <see cref="ConvergenceComponents.ContradictionAssessed"/> carries per candidate.
/// </summary>
public sealed record ConvergenceGauge
{
    public required Guid WorldId { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required Guid? AssessmentId { get; init; }

    /// <summary>Highest first, capped at <see cref="Services.ConvergenceWeights.MaxCandidates"/>.</summary>
    public required IReadOnlyList<ConvergenceCandidate> Candidates { get; init; }

    /// <summary>Candidates found before the cap, so a caller can say what it is not showing.</summary>
    public required int TotalCandidates { get; init; }
}
