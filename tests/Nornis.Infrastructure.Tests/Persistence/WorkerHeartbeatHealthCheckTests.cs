using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The thresholds are the whole check. Too tight and every redeploy pages someone; too
/// loose and a worker can be dead for an evening's session before the page admits it.
/// </summary>
[TestFixture]
public class WorkerHeartbeatHealthCheckTests
{
    private IWorkerHeartbeatRepository _repository = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IWorkerHeartbeatRepository>();
    }

    private Task<HealthCheckResult> CheckAsync(DateTimeOffset? lastBeat)
    {
        _repository.GetLastBeatAsync(WorkerHeartbeatHealthCheck.WorkerName, Arg.Any<CancellationToken>())
            .Returns(lastBeat);

        return new WorkerHeartbeatHealthCheck(_repository).CheckHealthAsync(new HealthCheckContext());
    }

    [Test]
    public async Task NeverBeaten_IsUnhealthy()
    {
        var result = await CheckAsync(null);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task BeatJustNow_IsHealthy()
    {
        var result = await CheckAsync(DateTimeOffset.UtcNow);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task BeatWithinTheDegradedThreshold_IsHealthy()
    {
        // A restart takes the worker offline for well under two minutes; that must stay green.
        var result = await CheckAsync(DateTimeOffset.UtcNow - WorkerHeartbeatHealthCheck.DegradedAfter + TimeSpan.FromSeconds(15));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task BeatPastTheDegradedThreshold_IsDegraded()
    {
        var result = await CheckAsync(DateTimeOffset.UtcNow - WorkerHeartbeatHealthCheck.DegradedAfter - TimeSpan.FromSeconds(15));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task BeatPastTheUnhealthyThreshold_IsUnhealthy()
    {
        var result = await CheckAsync(DateTimeOffset.UtcNow - WorkerHeartbeatHealthCheck.UnhealthyAfter - TimeSpan.FromMinutes(1));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }
}
