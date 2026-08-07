using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The score is pure, so every boundary it has is testable without a repository. These pin the
/// shape of each component and the one structural decision the total makes — that familiarity
/// multiplies rather than adds.
/// </summary>
[TestFixture]
public class ConvergenceScoreTests
{
    #region Components

    [TestCase(0, 0.0)]
    [TestCase(90, 0.5)]
    [TestCase(180, 1.0)]
    [TestCase(3600, 1.0)]
    public void Dormancy_SaturatesAtTheConfiguredHorizon(int daysHidden, double expected)
    {
        Assert.That(ConvergenceScore.Dormancy(daysHidden), Is.EqualTo(expected).Within(0.0001));
    }

    [TestCase(0, 0.0)]
    [TestCase(5, 1.0)]
    [TestCase(500, 1.0)]
    public void AnchorFamiliarity_SaturatesOnceThePartyPlainlyKnowsTheEntity(int facts, double expected)
    {
        Assert.That(ConvergenceScore.AnchorFamiliarity(facts), Is.EqualTo(expected).Within(0.0001));
    }

    [TestCase(0, 1.0)]
    [TestCase(1, 0.5)]
    [TestCase(3, 0.25)]
    public void SelfContainment_FallsOffWithEachArtifactDraggedAlong(int missing, double expected)
    {
        Assert.That(ConvergenceScore.SelfContainment(missing), Is.EqualTo(expected).Within(0.0001));
    }

    [Test]
    public void SelfContainment_TreatsANegativeCountAsZero()
    {
        // Not reachable through the service, but the function is public and a negative count
        // would otherwise divide toward infinity rather than clamping.
        Assert.That(ConvergenceScore.SelfContainment(-4), Is.EqualTo(1.0).Within(0.0001));
    }

    [Test]
    public void StorylineState_RanksAFinishedStorylineHighest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceScore.StorylineState(ArtifactStatus.Resolved), Is.GreaterThan(
                ConvergenceScore.StorylineState(ArtifactStatus.Dormant)));
            Assert.That(ConvergenceScore.StorylineState(ArtifactStatus.Dormant), Is.GreaterThan(
                ConvergenceScore.StorylineState(ArtifactStatus.Active)));
            Assert.That(ConvergenceScore.StorylineState(null), Is.EqualTo(0.0));
        });
    }

    [Test]
    public void ContradictionPressure_DistinguishesNotLookingFromFindingNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceScore.ContradictionPressure(null, assessed: false), Is.Null,
                "no assessment existed — the component is unavailable, not zero");
            Assert.That(ConvergenceScore.ContradictionPressure(null, assessed: true), Is.EqualTo(0.0),
                "an assessment existed and cited nothing — that is a measured zero");
            Assert.That(ConvergenceScore.ContradictionPressure(ContinuityFindingSeverity.High, assessed: true),
                Is.EqualTo(1.0));
        });
    }

    #endregion

    #region Total

    [Test]
    public void Total_IsZeroWhenNothingIsReady()
    {
        Assert.That(ConvergenceScore.Total(Components()), Is.Zero);
    }

    [Test]
    public void Total_IsOneHundredWhenEverySignalIsAtItsMaximum()
    {
        var everything = ConvergenceScore.Components(
            daysHidden: ConvergenceWeights.DormancySaturationDays,
            partyVisibleFactsOnAnchor: ConvergenceWeights.FamiliaritySaturationFacts,
            missingArtifactCount: 0,
            storylineStatus: ArtifactStatus.Resolved,
            contradictionSeverity: ContinuityFindingSeverity.High,
            contradictionAssessed: true);

        Assert.That(ConvergenceScore.Total(everything), Is.EqualTo(100));
    }

    [Test]
    public void Total_TreatsAnUnreadContradictionAsAbsentRatherThanAsAPenalty()
    {
        var assessedAndClean = ConvergenceScore.Components(
            90, 5, 0, ArtifactStatus.Active, contradictionSeverity: null, contradictionAssessed: true);
        var neverAssessed = ConvergenceScore.Components(
            90, 5, 0, ArtifactStatus.Active, contradictionSeverity: null, contradictionAssessed: false);

        // A world with no assessment should rank on what is known, not be flattened for what
        // was never measured — so the two score identically and only the component differs.
        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceScore.Total(neverAssessed), Is.EqualTo(ConvergenceScore.Total(assessedAndClean)));
            Assert.That(neverAssessed.ContradictionPressure, Is.Null);
            Assert.That(assessedAndClean.ContradictionPressure, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void Total_AppliesTheFamiliarityFloorRatherThanAnnihilatingAnUnknownEntity()
    {
        var onAnUnknownEntity = ConvergenceScore.Components(
            daysHidden: ConvergenceWeights.DormancySaturationDays,
            partyVisibleFactsOnAnchor: 0,
            missingArtifactCount: 0,
            storylineStatus: ArtifactStatus.Resolved,
            contradictionSeverity: ContinuityFindingSeverity.High,
            contradictionAssessed: true);

        Assert.That(ConvergenceScore.Total(onAnUnknownEntity), Is.GreaterThan(0),
            "not yet legible to anyone is not the same as not a secret");
    }

    #endregion

    #region Ordering

    [Test]
    public void Compare_OrdersByScoreThenOldestThenId()
    {
        var older = DateTimeOffset.UtcNow.AddDays(-10);
        var newer = DateTimeOffset.UtcNow;

        var high = Candidate(score: 80, createdAt: newer, id: Guid.Parse("00000000-0000-0000-0000-0000000000ff"));
        var lowOld = Candidate(score: 10, createdAt: older, id: Guid.Parse("00000000-0000-0000-0000-0000000000ee"));
        var lowNew = Candidate(score: 10, createdAt: newer, id: Guid.Parse("00000000-0000-0000-0000-000000000001"));

        var sorted = new List<ConvergenceCandidate> { lowNew, high, lowOld };
        sorted.Sort(ConvergenceScore.Compare);

        Assert.That(sorted, Is.EqualTo([high, lowOld, lowNew]).AsCollection);
    }

    [Test]
    public void Compare_IsTotalEvenWhenScoreAndAgeCollide()
    {
        // Determinism (Property 3) has to be a property of the comparison, not a hope about
        // score collisions — two candidates minted in the same transaction share a timestamp.
        var when = DateTimeOffset.UtcNow;
        var first = Candidate(50, when, Guid.Parse("00000000-0000-0000-0000-000000000001"));
        var second = Candidate(50, when, Guid.Parse("00000000-0000-0000-0000-000000000002"));

        Assert.Multiple(() =>
        {
            Assert.That(ConvergenceScore.Compare(first, second), Is.LessThan(0));
            Assert.That(ConvergenceScore.Compare(second, first), Is.GreaterThan(0));
        });
    }

    #endregion

    private static ConvergenceComponents Components() => ConvergenceScore.Components(
        daysHidden: 0,
        partyVisibleFactsOnAnchor: 0,
        missingArtifactCount: int.MaxValue,
        storylineStatus: null,
        contradictionSeverity: null,
        contradictionAssessed: true);

    private static ConvergenceCandidate Candidate(int score, DateTimeOffset createdAt, Guid id) => new()
    {
        Kind = ConvergenceCandidateKind.Fact,
        Id = id,
        AnchorArtifactId = Guid.NewGuid(),
        AnchorName = "Captain Voss",
        Description = "true allegiance: the Vespergale cult",
        CreatedAt = createdAt,
        MissingArtifactIds = [],
        Components = Components(),
        Score = score
    };
}
