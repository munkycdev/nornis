namespace Nornis.Application.Ai;

/// <summary>
/// Remembers how the last few AI calls went, so the status endpoint can report on Azure
/// OpenAI without calling it.
///
/// An active probe would be a paid request on every scrape, which buys nothing a passive
/// reading of real traffic does not already give. The cost is scope: this is per-process
/// memory, so the API's reading covers API-side calls (Loremaster, audit, retrospectives,
/// embeddings) and says nothing about extraction, which runs in the worker. A dead
/// extraction path shows up as a stale worker heartbeat instead.
/// </summary>
public interface IAiOutcomeMonitor
{
    void Record(bool succeeded, DateTimeOffset at);

    /// <summary>Outcomes within <paramref name="window"/> of <paramref name="now"/>.</summary>
    AiOutcomeSnapshot Snapshot(TimeSpan window, DateTimeOffset now);
}

/// <param name="Total">Calls observed in the window. Zero means idle, which is not a fault.</param>
/// <param name="Failures">How many of them failed.</param>
/// <param name="LastAt">When the most recent call landed, ignoring the window. Null if none ever has.</param>
public sealed record AiOutcomeSnapshot(int Total, int Failures, DateTimeOffset? LastAt);
