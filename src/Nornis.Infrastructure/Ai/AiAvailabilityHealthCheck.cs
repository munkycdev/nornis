using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Reports Azure OpenAI availability from observed traffic rather than a probe — see
/// <see cref="IAiOutcomeMonitor"/> for why a probe would be the wrong instrument.
///
/// Idle reads Healthy on purpose. Most of the time nobody is asking the Loremaster
/// anything, and a status page that goes amber overnight teaches people to ignore it.
/// </summary>
public class AiAvailabilityHealthCheck : IHealthCheck
{
    /// <summary>
    /// Long enough to still be judging the provider rather than one unlucky minute,
    /// short enough that recovery shows up without waiting for the next scrape hour.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IAiOutcomeMonitor _monitor;

    public AiAvailabilityHealthCheck(IAiOutcomeMonitor monitor)
    {
        _monitor = monitor;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var snapshot = _monitor.Snapshot(Window, DateTimeOffset.UtcNow);

        if (snapshot.Total == 0)
        {
            return Task.FromResult(HealthCheckResult.Healthy("No AI calls in the last 15 minutes."));
        }

        // Every observed call failing is the signal. A mixed result means the provider is
        // answering and something about particular requests is at fault — a content filter
        // rejection, a budget refusal — which is not an outage and must not read as one.
        return Task.FromResult(snapshot.Failures == snapshot.Total
            ? HealthCheckResult.Degraded($"The last {snapshot.Total} AI call(s) all failed.")
            : HealthCheckResult.Healthy($"{snapshot.Total - snapshot.Failures} of {snapshot.Total} recent AI call(s) succeeded."));
    }
}
