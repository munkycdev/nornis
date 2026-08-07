using Nornis.Application.Services;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Feature 22, Correctness Property 4. The marker's whole contract is two rules, and both exist
/// because of a way a reader loses reveals they should have seen.
/// </summary>
[TestFixture]
public class LearnedMarkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ANewMarker_TakesTheClaimedPoint()
    {
        var claimed = Now.AddHours(-1);

        Assert.That(LearnedMarker.Advance(null, claimed, Now), Is.EqualTo(claimed));
    }

    [Test]
    public void AnOlderClaim_LeavesTheMarkerWhereItIs()
    {
        // A second tab, or a client posting a list it fetched ten minutes ago, must not reopen
        // reveals the reader has already closed.
        var current = Now.AddHours(-1);

        Assert.That(LearnedMarker.Advance(current, Now.AddHours(-5), Now), Is.EqualTo(current));
    }

    [Test]
    public void ANewerClaim_MovesTheMarkerForward()
    {
        var claimed = Now.AddMinutes(-1);

        Assert.That(LearnedMarker.Advance(Now.AddHours(-1), claimed, Now), Is.EqualTo(claimed));
    }

    [Test]
    public void AFutureClaim_IsClampedToNow()
    {
        // A skewed clock would otherwise mark seen what has not happened, and there is no way
        // back from that — the reader simply never sees those entries.
        Assert.That(LearnedMarker.Advance(null, Now.AddDays(30), Now), Is.EqualTo(Now));
    }

    [Test]
    public void AFutureClaim_DoesNotDragAnExistingMarkerForward()
    {
        var current = Now.AddHours(-1);

        Assert.That(LearnedMarker.Advance(current, Now.AddDays(30), Now), Is.EqualTo(Now));
    }

    [Test]
    public void RepeatingAClaim_ChangesNothing()
    {
        var once = LearnedMarker.Advance(null, Now.AddHours(-1), Now);
        var twice = LearnedMarker.Advance(once, Now.AddHours(-1), Now);

        Assert.That(twice, Is.EqualTo(once), "marking seen is idempotent, not a conflict");
    }

    [Test]
    public void AnyOrderOfClaims_LandsOnTheLatest()
    {
        // Property 4 stated directly: order of arrival cannot change where the marker ends up.
        DateTimeOffset[] claims =
        [
            Now.AddHours(-5), Now.AddHours(-1), Now.AddHours(-9), Now.AddHours(-3)
        ];

        DateTimeOffset? forward = null;
        foreach (var claim in claims)
        {
            forward = LearnedMarker.Advance(forward, claim, Now);
        }

        DateTimeOffset? backward = null;
        foreach (var claim in claims.Reverse())
        {
            backward = LearnedMarker.Advance(backward, claim, Now);
        }

        Assert.That(forward, Is.EqualTo(backward).And.EqualTo(claims.Max()));
    }
}
