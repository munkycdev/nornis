using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The nav polls the activity endpoint so the processing and review badges move without the user
/// navigating. It used to do that forever in every open tab — and a browser left open overnight is
/// the normal case, not the exceptional one. Each poll is an API call and a database read, and
/// enough traffic to hold a scaled-to-zero container awake.
///
/// <para>These rules are worth their own tests because the failure is invisible: a badge looks the
/// same whether the poll is running at the right cadence, the wrong one, or in a tab nobody is
/// looking at. Nothing on screen would tell you, and the bill arrives a month later.</para>
/// </summary>
[TestFixture]
public class NavActivityCadenceTests
{
    [Test]
    public void AVisibleTabWithWorkInFlight_PollsFast()
    {
        Assert.That(NavActivityCadence.Interval(tabVisible: true, hasWorkInFlight: true),
            Is.EqualTo(NavActivityCadence.Active));
    }

    [Test]
    public void AVisibleIdleTab_PollsSlowly()
    {
        Assert.That(NavActivityCadence.Interval(tabVisible: true, hasWorkInFlight: false),
            Is.EqualTo(NavActivityCadence.Idle));
    }

    [Test]
    public void AHiddenTabNeverPollsFast_EvenWithWorkInFlight()
    {
        // The fast cadence exists to move a badge promptly. A background tab has no badge on
        // screen, so work in flight is not a reason to check four times a minute — and an
        // extraction running while the user is in another tab is exactly when it would.
        Assert.That(NavActivityCadence.Interval(tabVisible: false, hasWorkInFlight: true),
            Is.EqualTo(NavActivityCadence.Idle));
    }

    [Test]
    public void AHiddenIdleTab_PollsSlowly()
    {
        // The fourth corner of the truth table, so all four are pinned rather than three.
        Assert.That(NavActivityCadence.Interval(tabVisible: false, hasWorkInFlight: false),
            Is.EqualTo(NavActivityCadence.Idle));
    }

    [Test]
    public void ComingBackToTheTab_RefreshesImmediately()
    {
        // The one moment the badges are guaranteed to be stale: the poll has been parked for
        // however long the tab sat in the background, so what is on screen is arbitrarily old.
        Assert.That(NavActivityCadence.ShouldRefreshOnVisibilityChange(wasVisible: false, visible: true),
            Is.True);
    }

    [Test]
    public void LeavingTheTab_FetchesNothing()
    {
        Assert.That(NavActivityCadence.ShouldRefreshOnVisibilityChange(wasVisible: true, visible: false),
            Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ARepeatedVisibilityEvent_IsNotATransition(bool state)
    {
        // Browsers fire visibilitychange on things that are not a foreground/background swap —
        // window focus moves, some minimise paths. Treating every event as a return would turn a
        // burst of them into a burst of forced refreshes, which is the load this is shedding.
        Assert.That(NavActivityCadence.ShouldRefreshOnVisibilityChange(wasVisible: state, visible: state),
            Is.False);
    }

    [Test]
    public void TheSlowCadenceIsSlowEnoughToBeWorthHaving()
    {
        // Guards the constants themselves. Someone tuning the badge's responsiveness could close
        // the gap between these two without noticing that the idle cadence is what makes an
        // always-open tab affordable.
        Assert.That(NavActivityCadence.Idle, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(60)));
        Assert.That(NavActivityCadence.Active, Is.LessThan(NavActivityCadence.Idle));
    }

    // ------------------------------------------------------------------ the loop itself

    // These drive RunAsync with a fake wait, so the behaviour that actually sheds the load — a
    // hidden tick spending nothing — is observable without waiting ninety real seconds. Without
    // them, deleting the visibility check from the loop leaves every other test in this file and
    // in NavMenuTabVisibilityTests green.

    /// <summary>Records the intervals asked for and lets the test decide when each wait ends.</summary>
    private sealed class FakeClock
    {
        public List<TimeSpan> Waits { get; } = [];
        public int Fetches { get; private set; }

        /// <summary>Waits that report being cut short rather than elapsing, by iteration index.</summary>
        public HashSet<int> WakeOn { get; init; } = [];

        /// <summary>Stops the loop once this many waits have happened.</summary>
        public required int StopAfterWaits { get; init; }

        public CancellationTokenSource Cts { get; } = new();

        public Task<bool> WaitAsync(TimeSpan interval, CancellationToken ct)
        {
            Waits.Add(interval);

            if (Waits.Count >= StopAfterWaits)
            {
                Cts.Cancel();
            }

            return Task.FromResult(!WakeOn.Contains(Waits.Count - 1));
        }

        public Task FetchAsync()
        {
            Fetches++;
            return Task.CompletedTask;
        }
    }

    private static Task RunAsync(FakeClock clock, Func<bool> visible, bool workInFlight = false) =>
        NavActivityCadence.RunAsync(visible, () => workInFlight, clock.WaitAsync, clock.FetchAsync, clock.Cts.Token);

    [Test]
    public async Task AHiddenTabTicksButFetchesNothing()
    {
        var clock = new FakeClock { StopAfterWaits = 3 };

        await RunAsync(clock, visible: () => false);

        Assert.Multiple(() =>
        {
            Assert.That(clock.Fetches, Is.Zero, "a background tab must not spend a request");
            Assert.That(clock.Waits, Is.Not.Empty, "but the loop stays alive so it can resume");
        });
    }

    [Test]
    public async Task AVisibleTabFetchesOnEveryTick()
    {
        var clock = new FakeClock { StopAfterWaits = 3 };

        await RunAsync(clock, visible: () => true);

        // Two, not three: the third wait is the one that cancels, and a loop that fetched after
        // its token was cancelled would be issuing a request against a disposed component.
        Assert.That(clock.Fetches, Is.EqualTo(2));
        Assert.That(clock.Waits, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task VisibilityIsRereadAfterTheWait_NotOnlyBefore()
    {
        // The tab can go into the background during a ninety-second wait. Deciding to fetch on the
        // state from before the wait would spend the request anyway.
        var visible = true;
        var clock = new FakeClock { StopAfterWaits = 1 };

        await NavActivityCadence.RunAsync(
            () => visible,
            () => false,
            (interval, ct) => { visible = false; return clock.WaitAsync(interval, ct); },
            clock.FetchAsync,
            clock.Cts.Token);

        Assert.That(clock.Fetches, Is.Zero);
    }

    [Test]
    public async Task AHiddenTabWithWorkInFlight_StillWaitsTheSlowInterval()
    {
        var clock = new FakeClock { StopAfterWaits = 2 };

        await RunAsync(clock, visible: () => false, workInFlight: true);

        Assert.That(clock.Waits, Is.All.EqualTo(NavActivityCadence.Idle));
    }

    [Test]
    public async Task ATabThatComesBackMidWait_PicksUpTheFastCadenceImmediately()
    {
        // The bug this closes: the loop starts a ninety-second wait while hidden, the user returns
        // a second later, and the badges then crawl for the rest of that wait even though an
        // extraction is running. Waking has to re-evaluate rather than let the old wait finish.
        var visible = false;
        var clock = new FakeClock { StopAfterWaits = 2, WakeOn = { 0 } };

        await NavActivityCadence.RunAsync(
            () => visible,
            () => true,
            (interval, ct) => { visible = true; return clock.WaitAsync(interval, ct); },
            clock.FetchAsync,
            clock.Cts.Token);

        Assert.Multiple(() =>
        {
            Assert.That(clock.Waits[0], Is.EqualTo(NavActivityCadence.Idle), "hidden when it started");
            Assert.That(clock.Waits[1], Is.EqualTo(NavActivityCadence.Active), "visible when it woke");
        });
    }

    [Test]
    public async Task AWokenWaitDoesNotCountAsATick()
    {
        // Whatever woke the loop has already refreshed. Fetching here too would double it.
        var clock = new FakeClock { StopAfterWaits = 1, WakeOn = { 0 } };

        await RunAsync(clock, visible: () => true);

        Assert.That(clock.Fetches, Is.Zero);
    }

    [Test]
    public async Task CancellationEndsTheLoopWithoutThrowing()
    {
        var clock = new FakeClock { StopAfterWaits = 1 };
        await clock.Cts.CancelAsync();

        Assert.DoesNotThrowAsync(async () => await RunAsync(clock, visible: () => true));

        Assert.That(clock.Fetches, Is.Zero);
    }

    [Test]
    public async Task ACancellingWaitStopsTheLoopRatherThanFetchingOneMoreTime()
    {
        // Disposal lands mid-wait far more often than between iterations.
        var clock = new FakeClock { StopAfterWaits = 1 };

        await RunAsync(clock, visible: () => true);

        Assert.That(clock.Fetches, Is.Zero, "the token was cancelled during the wait that just ended");
    }
}
