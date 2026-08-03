using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ContinuityAuditScoringTests
{
    [Test]
    public void PenaltyFor_UsesSpecifiedWeights()
    {
        Assert.That(ContinuityAuditService.PenaltyFor(ContinuityFindingSeverity.High), Is.EqualTo(12));
        Assert.That(ContinuityAuditService.PenaltyFor(ContinuityFindingSeverity.Medium), Is.EqualTo(6));
        Assert.That(ContinuityAuditService.PenaltyFor(ContinuityFindingSeverity.Low), Is.EqualTo(2));
    }

    [Test]
    public void TotalPenalty_SumsWeights()
    {
        var severities = new[]
        {
            ContinuityFindingSeverity.High,
            ContinuityFindingSeverity.Medium,
            ContinuityFindingSeverity.Low
        };

        Assert.That(ContinuityAuditService.TotalPenalty(severities), Is.EqualTo(20));
    }

    [Test]
    public void TotalPenalty_IsCappedAt40()
    {
        // 5 High = 60, capped to 40.
        var severities = Enumerable.Repeat(ContinuityFindingSeverity.High, 5);

        Assert.That(ContinuityAuditService.TotalPenalty(severities), Is.EqualTo(40));
    }

    [Test]
    public void BlendScore_SubtractsCappedPenaltyFromHeuristic()
    {
        // heuristic 90, one High (-12) and one Medium (-6) = -18 -> 72.
        var severities = new[] { ContinuityFindingSeverity.High, ContinuityFindingSeverity.Medium };

        Assert.That(ContinuityAuditService.BlendScore(90, severities), Is.EqualTo(72));
    }

    [Test]
    public void BlendScore_FloorsAtZero()
    {
        var severities = Enumerable.Repeat(ContinuityFindingSeverity.High, 5); // penalty capped 40

        Assert.That(ContinuityAuditService.BlendScore(10, severities), Is.EqualTo(0));
    }

    [Test]
    public void BlendScore_NoFindings_EqualsHeuristic()
    {
        Assert.That(ContinuityAuditService.BlendScore(83, []), Is.EqualTo(83));
    }

    [Test]
    public void BlendScore_ExcludingDismissedRaisesScore()
    {
        // Effective score is computed from OPEN severities only. Dropping a dismissed High
        // from the list (as the service does) raises the score by that finding's penalty.
        var allOpen = new[] { ContinuityFindingSeverity.High, ContinuityFindingSeverity.Low };
        var afterDismissHigh = new[] { ContinuityFindingSeverity.Low };

        var before = ContinuityAuditService.BlendScore(80, allOpen);
        var after = ContinuityAuditService.BlendScore(80, afterDismissHigh);

        Assert.That(after - before, Is.EqualTo(12));
    }

    #region The breakdown the Web renders

    [Test]
    public void Breakdown_AgreesWithTotalPenalty()
    {
        var severities = new[]
        {
            ContinuityFindingSeverity.High,
            ContinuityFindingSeverity.High,
            ContinuityFindingSeverity.Low,
        };

        var breakdown = ContinuityAuditService.BuildPenaltyBreakdown(severities, staleSuspendedCount: 0);

        // The itemised version and the scalar version are two renderings of one rule. The Web
        // renders the itemised one, so a divergence would show a total that does not match the
        // score beside it — which is exactly what having two copies of the rule used to risk.
        Assert.Multiple(() =>
        {
            Assert.That(breakdown.CappedPenalty, Is.EqualTo(ContinuityAuditService.TotalPenalty(severities)));
            Assert.That(breakdown.Lines.Sum(l => l.Subtotal), Is.EqualTo(breakdown.RawPenalty));
        });
    }

    [Test]
    public void Breakdown_ListsOnlySeveritiesPresent_WorstFirst()
    {
        var breakdown = ContinuityAuditService.BuildPenaltyBreakdown(
            [ContinuityFindingSeverity.Low, ContinuityFindingSeverity.High], staleSuspendedCount: 0);

        Assert.That(breakdown.Lines.Select(l => l.Severity), Is.EqualTo(["High", "Low"]));
    }

    [Test]
    public void Breakdown_ReportsTheCapAndWhetherItBit()
    {
        // Four Highs is 48, over the cap of 40.
        var overCap = Enumerable.Repeat(ContinuityFindingSeverity.High, 4).ToList();

        var breakdown = ContinuityAuditService.BuildPenaltyBreakdown(overCap, staleSuspendedCount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(breakdown.RawPenalty, Is.EqualTo(48));
            Assert.That(breakdown.CappedPenalty, Is.EqualTo(ContinuityAuditService.PenaltyCap));
            Assert.That(breakdown.IsCapped, Is.True);
        });
    }

    [Test]
    public void Breakdown_CarriesTheWholeScaleEvenWhenNothingScored()
    {
        var breakdown = ContinuityAuditService.BuildPenaltyBreakdown([], staleSuspendedCount: 0);

        // The page states the rule ("High 12, Medium 6, Low 2") as well as applying it, and a
        // world with no findings still has to be able to state it.
        Assert.Multiple(() =>
        {
            Assert.That(breakdown.Lines, Is.Empty);
            Assert.That(
                breakdown.Scale.Select(s => (s.Severity, s.PenaltyEach)),
                Is.EqualTo([("High", 12), ("Medium", 6), ("Low", 2)]));
        });
    }

    [Test]
    public void Breakdown_ScaleMatchesPenaltyFor()
    {
        // The scale is derived, not typed twice — this fails if someone reintroduces a literal.
        Assert.That(
            ContinuityAuditService.SeverityScale.Select(s => s.PenaltyEach),
            Is.EqualTo(ContinuityAuditService.PenaltySeverities.Select(ContinuityAuditService.PenaltyFor)));
    }

    #endregion
}
