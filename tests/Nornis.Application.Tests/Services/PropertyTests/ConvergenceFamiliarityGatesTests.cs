using FsCheck;
using FsCheck.Fluent;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Feature 21, Correctness Property 5: familiarity gates, it does not merely add.
///
/// The design chose to multiply the weighted sum by anchor familiarity rather than add it as a
/// fifth term. The claim that decision rests on is quantified here: no amount of the other
/// signals can carry a secret on an entity the party has never met above a familiar,
/// contradicted one. An additive term would fail this for a sufficiently old secret, which is
/// the ranking a GM would never agree with.
/// </summary>
[TestFixture]
[Category("Feature: convergence-gauge, Property 5: Familiarity gates rather than adds")]
public class ConvergenceFamiliarityGatesTests
{
    /// <summary>
    /// The gate stated as the bound it actually is: whatever else is true of a secret, if the
    /// party has no visible facts on the entity it hangs from, its score cannot exceed the
    /// familiarity floor's share. Multiplying gives that ceiling for free; adding cannot give
    /// it at all, which is what makes this the discriminating form. An earlier version of this
    /// property compared against one hand-picked rival and passed under both formulations —
    /// a guard that could not fire.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 500)]
    [Description("Feature: convergence-gauge, Property 5 — an unknown entity is bounded by the familiarity floor")]
    public Property UnknownEntity_IsBoundedByTheFamiliarityFloor()
    {
        var anySignals =
            from days in Gen.Choose(0, 4000)
            from missing in Gen.Choose(0, 6)
            from status in Gen.Elements<ArtifactStatus?>(
                ArtifactStatus.Active, ArtifactStatus.Dormant, ArtifactStatus.Resolved, null)
            from severity in Gen.Elements<ContinuityFindingSeverity?>(
                ContinuityFindingSeverity.High, ContinuityFindingSeverity.Medium,
                ContinuityFindingSeverity.Low, null)
            select ConvergenceScore.Components(
                daysHidden: days,
                partyVisibleFactsOnAnchor: 0,
                missingArtifactCount: missing,
                storylineStatus: status,
                contradictionSeverity: severity,
                contradictionAssessed: true);

        var ceiling = (int)Math.Round(ConvergenceWeights.FamiliarityFloor * 100, MidpointRounding.AwayFromZero);

        return Prop.ForAll(anySignals.ToArbitrary(), components =>
            ConvergenceScore.Total(components) <= ceiling);
    }

    [FsCheck.NUnit.Property(MaxTest = 200)]
    [Description("Feature: convergence-gauge, Property 5 — a familiar contradicted candidate clears that ceiling")]
    public Property FamiliarContradictedCandidate_ClearsTheUnknownCeiling()
    {
        // The bound above is only meaningful if ordinary familiar candidates sit above it —
        // otherwise the gate would be flattening everything rather than ranking.
        var familiar = Gen.Choose(ConvergenceWeights.FamiliaritySaturationFacts, 50)
            .Select(facts => ConvergenceScore.Components(
                daysHidden: 0,
                partyVisibleFactsOnAnchor: facts,
                missingArtifactCount: 0,
                storylineStatus: null,
                contradictionSeverity: ContinuityFindingSeverity.High,
                contradictionAssessed: true));

        var ceiling = (int)Math.Round(ConvergenceWeights.FamiliarityFloor * 100, MidpointRounding.AwayFromZero);

        return Prop.ForAll(familiar.ToArbitrary(), components =>
            ConvergenceScore.Total(components) > ceiling);
    }

    [FsCheck.NUnit.Property(MaxTest = 200)]
    [Description("Feature: convergence-gauge, Property 5 — more familiarity never lowers a score")]
    public Property MoreFamiliarity_NeverLowersAScore()
    {
        var pairs = from lower in Gen.Choose(0, 20)
                    from delta in Gen.Choose(0, 20)
                    select (lower, higher: lower + delta);

        return Prop.ForAll(pairs.ToArbitrary(), pair =>
        {
            var (lower, higher) = pair;

            var less = ConvergenceScore.Components(90, lower, 1, ArtifactStatus.Dormant, ContinuityFindingSeverity.Medium, true);
            var more = ConvergenceScore.Components(90, higher, 1, ArtifactStatus.Dormant, ContinuityFindingSeverity.Medium, true);

            return ConvergenceScore.Total(more) >= ConvergenceScore.Total(less);
        });
    }
}
