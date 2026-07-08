using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ContinuityAuditEligibilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Quiet = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinInterval = TimeSpan.FromHours(20);

    private static bool Eligible(DateTimeOffset? acceptance, DateTimeOffset? assessment) =>
        ContinuityAuditEligibility.IsEligible(acceptance, assessment, Now, Quiet, MinInterval);

    [Test]
    public void NoAcceptances_NotEligible()
    {
        Assert.That(Eligible(acceptance: null, assessment: null), Is.False);
    }

    [Test]
    public void AcceptanceWithinQuietPeriod_NotEligible()
    {
        // Acceptance is newer than the last assessment, but only 30 minutes old (< 1h quiet period).
        var acceptance = Now.AddMinutes(-30);
        var assessment = Now.AddHours(-5);

        Assert.That(Eligible(acceptance, assessment), Is.False);
    }

    [Test]
    public void SettledAcceptanceNewerThanLastAssessment_Eligible()
    {
        var acceptance = Now.AddHours(-2);   // past the quiet period
        var assessment = Now.AddHours(-25);  // last run > 20h ago

        Assert.That(Eligible(acceptance, assessment), Is.True);
    }

    [Test]
    public void NeverAssessedButSettledAcceptance_Eligible()
    {
        var acceptance = Now.AddHours(-2);

        Assert.That(Eligible(acceptance, assessment: null), Is.True);
    }

    [Test]
    public void AssessedWithinMinInterval_NotEligible()
    {
        // Settled acceptance newer than the last assessment, but that assessment ran only 10h ago.
        var acceptance = Now.AddHours(-2);
        var assessment = Now.AddHours(-10);

        Assert.That(Eligible(acceptance, assessment), Is.False);
    }

    [Test]
    public void AcceptanceOlderThanLastAssessment_NotEligible()
    {
        // Already assessed after the most recent acceptance — nothing new to look at.
        var acceptance = Now.AddHours(-25);
        var assessment = Now.AddHours(-2);

        Assert.That(Eligible(acceptance, assessment), Is.False);
    }
}
