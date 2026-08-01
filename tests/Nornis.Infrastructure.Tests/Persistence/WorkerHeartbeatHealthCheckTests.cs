using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Domain.Repositories;
using Nornis.Infrastructure.Persistence;
using NSubstitute;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Persistence;

/// <summary>
/// The check's whole subtlety is that an absent worker is usually correct. ca-nornis-worker
/// scales to zero when the queue is empty, so heartbeat silence only means something is
/// wrong if there was work outstanding. The first version of this check missed that and
/// reported the platform Unhealthy within minutes of going idle — these tests exist so it
/// cannot happen again.
/// </summary>
[TestFixture]
public class WorkerHeartbeatHealthCheckTests
{
    private IWorkerHeartbeatRepository _heartbeats = null!;
    private ISourceRepository _sources = null!;

    [SetUp]
    public void SetUp()
    {
        _heartbeats = Substitute.For<IWorkerHeartbeatRepository>();
        _sources = Substitute.For<ISourceRepository>();
    }

    private Task<HealthCheckResult> CheckAsync(int pending, DateTimeOffset? lastBeat)
    {
        _sources.CountAwaitingExtractionAsync(Arg.Any<CancellationToken>()).Returns(pending);
        _heartbeats.GetLastBeatAsync(WorkerHeartbeatHealthCheck.WorkerName, Arg.Any<CancellationToken>())
            .Returns(lastBeat);

        return new WorkerHeartbeatHealthCheck(_heartbeats, _sources)
            .CheckHealthAsync(new HealthCheckContext());
    }

    private static DateTimeOffset MinutesAgo(double minutes) =>
        DateTimeOffset.UtcNow - TimeSpan.FromMinutes(minutes);

    [Test]
    public async Task NoWorkPending_AndWorkerLongGone_IsHealthy()
    {
        // The everyday case: nobody has uploaded anything, so the worker has scaled to zero
        // exactly as configured. This is the regression that made /status 503 on an idle
        // system for the first hour it existed.
        var result = await CheckAsync(pending: 0, lastBeat: MinutesAgo(180));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task NoWorkPending_AndWorkerNeverSeen_IsHealthy()
    {
        var result = await CheckAsync(pending: 0, lastBeat: null);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task WorkPending_AndWorkerActive_IsHealthy()
    {
        var result = await CheckAsync(pending: 3, lastBeat: DateTimeOffset.UtcNow);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task WorkPending_AndWorkerScalingUp_IsDegradedNotUnhealthy()
    {
        // A worker cold-starting to meet this very work. Amber, because nothing has
        // actually failed yet — and Degraded keeps /status on 200.
        var result = await CheckAsync(pending: 1, lastBeat: MinutesAgo(5));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task WorkPending_AndWorkerSilentPastTheLimit_IsUnhealthy()
    {
        // Fifteen minutes of outstanding work with no heartbeat is not a slow start.
        var result = await CheckAsync(pending: 1, lastBeat: MinutesAgo(20));

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task WorkPending_AndWorkerNeverSeen_IsUnhealthy()
    {
        var result = await CheckAsync(pending: 1, lastBeat: null);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    [Test]
    public async Task TheDescription_SaysHowMuchWorkIsWaiting()
    {
        var result = await CheckAsync(pending: 7, lastBeat: MinutesAgo(20));

        // The count is the actionable part — "the worker is down" and "the worker is down
        // and 7 sessions are stuck behind it" are different mornings.
        Assert.That(result.Description, Does.Contain("7"));
    }
}
