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
}
