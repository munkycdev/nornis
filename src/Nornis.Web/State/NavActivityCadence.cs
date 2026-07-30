namespace Nornis.Web.State;

/// <summary>
/// The nav's activity-badge poll: how often it runs, whether a given tick does anything, and the
/// loop itself.
///
/// <para>The whole thing lives here rather than in <c>NavMenu</c> because it is the part that goes
/// wrong quietly. The badge looks identical whether the poll is running at the right cadence, the
/// wrong one, or in a tab nobody is looking at — nothing on screen would tell you, and the symptom
/// is a bill and a container that never scales to zero. Keeping the loop out of the component is
/// what lets it be driven by a fake clock instead of by waiting ninety real seconds.</para>
/// </summary>
public static class NavActivityCadence
{
    /// <summary>Cadence while something is actually moving.</summary>
    public static readonly TimeSpan Active = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Cadence when the world is idle. Nothing changes these numbers without either this browser
    /// acting — which raises <c>ActivitySignal</c> and refreshes immediately — or the worker
    /// finishing something, which only happens when work was queued.
    /// </summary>
    public static readonly TimeSpan Idle = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Cadence while the session is expired: the API has rejected the token and every poll at the
    /// normal rate would 401 the same way — the storm this replaces ran at ~400 unauthorized
    /// requests over three hours. A slow probe is kept, rather than stopping outright, because
    /// each attempt goes back through the token refresher: when the failure was transient the
    /// probe is what notices recovery and restores the normal cadence without the user doing
    /// anything.
    /// </summary>
    public static readonly TimeSpan Expired = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The wait before the next tick.
    ///
    /// <para>An expired session overrides everything: work in flight is not a reason to hammer an
    /// API that has already said no. A hidden tab holds the slow cadence whatever the world is
    /// doing: the fast one exists to move a badge promptly, and a background tab has no badge on
    /// screen to move.</para>
    /// </summary>
    public static TimeSpan Interval(bool tabVisible, bool hasWorkInFlight, bool sessionExpired) =>
        sessionExpired ? Expired
        : tabVisible && hasWorkInFlight ? Active
        : Idle;

    /// <summary>
    /// Whether becoming <paramref name="visible"/> means the badges need re-reading now.
    ///
    /// <para>Only on the way back, and only as a real transition. The poll has been parked for
    /// however long the tab sat in the background, so what is on screen is arbitrarily old — this
    /// is the one refresh that must skip the freshness window. Going away needs nothing, and
    /// browsers fire <c>visibilitychange</c> on things that are not a foreground/background swap,
    /// so treating every event as a return would turn a burst of them into a burst of refreshes.
    /// </para>
    /// </summary>
    public static bool ShouldRefreshOnVisibilityChange(bool wasVisible, bool visible) =>
        visible && !wasVisible;

    /// <summary>
    /// Runs the poll until <paramref name="ct"/> is cancelled.
    /// </summary>
    /// <param name="isTabVisible">
    /// Read fresh on both sides of the wait, because it changes underneath: the browser can
    /// background the tab at any point, including while this is waiting.
    /// </param>
    /// <param name="waitAsync">
    /// Waits out one interval. Returns false if the wait was cut short — the tab came back and the
    /// cadence needs recomputing. Without that, a tab returning to the foreground would stay on
    /// the slow cadence until the ninety-second wait it was already inside finished, which is
    /// exactly the case the fast cadence exists for.
    /// </param>
    public static async Task RunAsync(
        Func<bool> isTabVisible,
        Func<bool> hasWorkInFlight,
        Func<bool> isSessionExpired,
        Func<TimeSpan, CancellationToken, Task<bool>> waitAsync,
        Func<Task> fetchAsync,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var interval = Interval(isTabVisible(), hasWorkInFlight(), isSessionExpired());

                if (!await waitAsync(interval, ct))
                {
                    // Woken rather than elapsed. Go straight back round to pick the new cadence —
                    // whatever woke us has already refreshed anything that needed refreshing.
                    continue;
                }

                if (ct.IsCancellationRequested)
                {
                    return;
                }

                // The tick fires while hidden — the loop stays alive so it resumes without being
                // restarted — but it spends nothing. This is the saving: the request and the query
                // behind it, in every backgrounded tab, for as long as the browser is open.
                if (!isTabVisible())
                {
                    continue;
                }

                await fetchAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Component disposed.
        }
    }
}
