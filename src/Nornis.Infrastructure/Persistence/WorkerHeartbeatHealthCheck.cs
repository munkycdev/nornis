using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Reports whether extraction work is actually being drained.
///
/// The first version of this check read heartbeat freshness alone and treated silence as
/// death. That was wrong for this deployment: <c>ca-nornis-worker</c> runs at
/// <c>minReplicas 0</c> and scales up on queue depth, so an idle worker is *correct* — and
/// within minutes of a quiet system the check reported the whole platform Unhealthy. A
/// status page that is red whenever nothing is happening is a status page nobody reads.
///
/// So silence is only evidence of failure when there was something to do. With outstanding
/// work the worker ought to be awake, and a stale heartbeat then means it is not coming.
///
/// The thresholds do the job a "queued at" timestamp would do more precisely. There is no
/// such column, and adding one to answer this would be a schema change in service of a
/// monitoring check; the heartbeat's own age is a good enough clock, because a scaler that
/// polls every 30 seconds does not take fifteen minutes to start a container.
/// </summary>
public class WorkerHeartbeatHealthCheck : IHealthCheck
{
    /// <summary>Matches the name the worker beats under; see HeartbeatWorker.</summary>
    public const string WorkerName = "nornis-worker";

    /// <summary>Comfortably past the worker's ~60s beat interval, so one slow write never trips it.</summary>
    public static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Long enough that scaling from zero, pulling an image and connecting cannot reach it,
    /// so crossing this line with work outstanding is unambiguous rather than a slow start.
    /// </summary>
    public static readonly TimeSpan UnhealthyAfter = TimeSpan.FromMinutes(15);

    private readonly IWorkerHeartbeatRepository _heartbeats;
    private readonly ISourceRepository _sources;

    public WorkerHeartbeatHealthCheck(IWorkerHeartbeatRepository heartbeats, ISourceRepository sources)
    {
        _heartbeats = heartbeats;
        _sources = sources;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var pending = await _sources.CountAwaitingExtractionAsync(cancellationToken);
        var lastBeat = await _heartbeats.GetLastBeatAsync(WorkerName, cancellationToken);

        if (pending == 0)
        {
            // Nothing to drain. Whether the worker happens to be running is not a fault
            // either way, so do not manufacture one.
            return HealthCheckResult.Healthy(
                lastBeat is null
                    ? "No extraction work pending."
                    : $"No extraction work pending; worker last seen {Describe(DateTimeOffset.UtcNow - lastBeat.Value)} ago.");
        }

        if (lastBeat is null)
        {
            return HealthCheckResult.Unhealthy($"{pending} source(s) awaiting extraction and the worker has never reported in.");
        }

        var age = DateTimeOffset.UtcNow - lastBeat.Value;

        if (age >= UnhealthyAfter)
        {
            return HealthCheckResult.Unhealthy(
                $"{pending} source(s) awaiting extraction; worker last seen {Describe(age)} ago.");
        }

        if (age >= DegradedAfter)
        {
            // Most likely the worker is scaling up to meet this very work. Amber says
            // "watch this" without claiming a failure that has not happened yet.
            return HealthCheckResult.Degraded(
                $"{pending} source(s) awaiting extraction; worker last seen {Describe(age)} ago.");
        }

        return HealthCheckResult.Healthy($"{pending} source(s) awaiting extraction; worker is active.");
    }

    private static string Describe(TimeSpan age) =>
        age < TimeSpan.FromMinutes(1)
            ? $"{age.TotalSeconds:F0}s"
            : $"{age.TotalMinutes:F0}m";
}
