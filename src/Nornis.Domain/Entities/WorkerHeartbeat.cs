namespace Nornis.Domain.Entities;

/// <summary>
/// The last time a background host reported itself alive.
///
/// The worker is a generic host with no HTTP surface, so "is the worker running" cannot be
/// answered by probing it. It answers by writing here instead, and the API's status check
/// reads the freshness. Without this, a dead worker is invisible: sources sit Queued and
/// nothing anywhere says why.
///
/// One row per host name rather than per instance — the question is whether *anything* is
/// draining the queue, which any replica answers on behalf of all of them.
/// </summary>
public class WorkerHeartbeat
{
    /// <summary>Host name, e.g. <c>nornis-worker</c>. Primary key: one row per host.</summary>
    public string WorkerName { get; set; } = string.Empty;

    public DateTimeOffset BeatAt { get; set; }
}
