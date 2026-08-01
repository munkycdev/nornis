using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence;

/// <summary>
/// Reports how long ago the worker last said it was alive.
///
/// This is the check the status page exists for. A dead worker breaks nothing the API can
/// see — requests succeed, pages render, sources enqueue — they just never get extracted,
/// and the only symptom is a queue that quietly stops moving.
/// </summary>
public class WorkerHeartbeatHealthCheck : IHealthCheck
{
    /// <summary>Matches the name the worker beats under; see HeartbeatWorker.</summary>
    public const string WorkerName = "nornis-worker";

    /// <summary>
    /// Comfortably past the worker's ~60s beat interval, so one slow write or a restart
    /// does not read as an outage.
    /// </summary>
    public static readonly TimeSpan DegradedAfter = TimeSpan.FromMinutes(2);

    /// <summary>Past this, the gap is longer than any redeploy and something is actually wrong.</summary>
    public static readonly TimeSpan UnhealthyAfter = TimeSpan.FromMinutes(5);

    private readonly IWorkerHeartbeatRepository _repository;

    public WorkerHeartbeatHealthCheck(IWorkerHeartbeatRepository repository)
    {
        _repository = repository;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var lastBeat = await _repository.GetLastBeatAsync(WorkerName, cancellationToken);

        if (lastBeat is null)
        {
            return HealthCheckResult.Unhealthy("The worker has never reported in.");
        }

        var age = DateTimeOffset.UtcNow - lastBeat.Value;

        if (age >= UnhealthyAfter)
        {
            return HealthCheckResult.Unhealthy($"Last heartbeat was {age.TotalMinutes:F0} minute(s) ago.");
        }

        return age >= DegradedAfter
            ? HealthCheckResult.Degraded($"Last heartbeat was {age.TotalMinutes:F0} minute(s) ago.")
            : HealthCheckResult.Healthy($"Last heartbeat was {age.TotalSeconds:F0} second(s) ago.");
    }
}
