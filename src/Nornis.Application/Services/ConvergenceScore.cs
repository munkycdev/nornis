using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The convergence score, and the only place it is computed. Pure: every input is passed in,
/// so the whole of the ranking's behaviour is testable without a repository, and no caller —
/// least of all a Razor page — has cause to recompute a component and drift from it.
/// </summary>
public static class ConvergenceScore
{
    /// <summary>Days a candidate has been hidden, saturating at
    /// <see cref="ConvergenceWeights.DormancySaturationDays"/>.</summary>
    public static double Dormancy(int daysHidden) =>
        Clamp01((double)daysHidden / ConvergenceWeights.DormancySaturationDays);

    /// <summary>How well the party already knows the anchoring artifact.</summary>
    public static double AnchorFamiliarity(int partyVisibleFactsOnAnchor) =>
        Clamp01((double)partyVisibleFactsOnAnchor / ConvergenceWeights.FamiliaritySaturationFacts);

    /// <summary>
    /// Falls off with each artifact that must be revealed alongside. Reciprocal rather than
    /// linear: the step from "nothing else" to "one other thing" is the one a GM feels, and
    /// the difference between eight and nine is noise.
    /// </summary>
    public static double SelfContainment(int missingArtifactCount) =>
        1.0 / (1 + Math.Max(0, missingArtifactCount));

    /// <summary>
    /// A secret still hidden under a storyline the table has finished is the clearest case of
    /// a reveal that was missed rather than withheld, so Resolved scores highest.
    /// </summary>
    public static double StorylineState(ArtifactStatus? status) => status switch
    {
        ArtifactStatus.Resolved => 1.0,
        ArtifactStatus.Dormant => 0.6,
        ArtifactStatus.Active => 0.3,
        // Archived anchors are excluded upstream; no storyline at all scores nothing.
        _ => 0.0
    };

    /// <summary>
    /// Null when there was no assessment to read, which is not the same as an assessment that
    /// cited nothing — see <see cref="ConvergenceComponents.ContradictionAssessed"/>.
    /// </summary>
    public static double? ContradictionPressure(ContinuityFindingSeverity? severity, bool assessed)
    {
        if (!assessed)
        {
            return null;
        }

        return severity switch
        {
            ContinuityFindingSeverity.High => 1.0,
            ContinuityFindingSeverity.Medium => 0.6,
            ContinuityFindingSeverity.Low => 0.3,
            _ => 0.0
        };
    }

    /// <summary>
    /// Builds the component set from raw observations, so the mapping from "what we saw" to
    /// "what it is worth" exists once.
    /// </summary>
    public static ConvergenceComponents Components(
        int daysHidden,
        int partyVisibleFactsOnAnchor,
        int missingArtifactCount,
        ArtifactStatus? storylineStatus,
        ContinuityFindingSeverity? contradictionSeverity,
        bool contradictionAssessed) => new()
        {
            DaysHidden = daysHidden,
            PartyVisibleFactsOnAnchor = partyVisibleFactsOnAnchor,
            MissingArtifactCount = missingArtifactCount,
            StorylineStatus = storylineStatus,
            ContradictionSeverity = contradictionSeverity,
            ContradictionAssessed = contradictionAssessed,
            Dormancy = Dormancy(daysHidden),
            AnchorFamiliarity = AnchorFamiliarity(partyVisibleFactsOnAnchor),
            SelfContainment = SelfContainment(missingArtifactCount),
            StorylineState = StorylineState(storylineStatus),
            ContradictionPressure = ContradictionPressure(contradictionSeverity, contradictionAssessed)
        };

    /// <summary>
    /// 0–100, matching the continuity score's convention.
    ///
    /// Familiarity multiplies the weighted sum instead of joining it. As one more addend, a
    /// long-enough-hidden secret on an entity the party has never met would climb to the top on
    /// age alone, which is the one ranking a GM would never agree with; as a multiplier with a
    /// floor it sinks without disappearing. An unread contradiction contributes nothing rather
    /// than penalising — a world with no assessment should rank on what is known, not be
    /// flattened for what was never measured.
    /// </summary>
    public static int Total(ConvergenceComponents components)
    {
        var weighted =
            ConvergenceWeights.Contradiction * (components.ContradictionPressure ?? 0.0)
            + ConvergenceWeights.SelfContainment * components.SelfContainment
            + ConvergenceWeights.Dormancy * components.Dormancy
            + ConvergenceWeights.Storyline * components.StorylineState;

        var familiarity = Math.Max(components.AnchorFamiliarity, ConvergenceWeights.FamiliarityFloor);

        return (int)Math.Round(Clamp01(weighted * familiarity) * 100, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Highest score first, then oldest, then by id. The last two make the order total for any
    /// data at all, so "same world state, same ranking" is a property of the comparison rather
    /// than a hope about score collisions.
    /// </summary>
    public static int Compare(ConvergenceCandidate left, ConvergenceCandidate right)
    {
        var byScore = right.Score.CompareTo(left.Score);
        if (byScore != 0)
        {
            return byScore;
        }

        var byAge = left.CreatedAt.CompareTo(right.CreatedAt);
        return byAge != 0 ? byAge : left.Id.CompareTo(right.Id);
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);
}
