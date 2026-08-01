using Microsoft.Extensions.Diagnostics.HealthChecks;
using Nornis.Application.Services;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Two judgments this check has to get right, because both would train people to stop
/// reading the page: an idle night is not an outage, and a content-filter rejection among
/// successful calls is not an outage either.
/// </summary>
[TestFixture]
public class AiAvailabilityHealthCheckTests
{
    private static Task<HealthCheckResult> CheckAsync(AiOutcomeMonitor monitor) =>
        new AiAvailabilityHealthCheck(monitor).CheckHealthAsync(new HealthCheckContext());

    [Test]
    public async Task NoRecentCalls_IsHealthy()
    {
        var result = await CheckAsync(new AiOutcomeMonitor());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task OnlyStaleCalls_IsHealthy()
    {
        var monitor = new AiOutcomeMonitor();
        monitor.Record(succeeded: false, DateTimeOffset.UtcNow - AiAvailabilityHealthCheck.Window - TimeSpan.FromMinutes(1));

        // Failures that fell out of the window are history, not a live outage.
        var result = await CheckAsync(monitor);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }

    [Test]
    public async Task EveryRecentCallFailed_IsDegraded()
    {
        var monitor = new AiOutcomeMonitor();
        monitor.Record(succeeded: false, DateTimeOffset.UtcNow);
        monitor.Record(succeeded: false, DateTimeOffset.UtcNow);

        var result = await CheckAsync(monitor);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }

    [Test]
    public async Task SomeRecentCallsSucceeded_IsHealthy()
    {
        var monitor = new AiOutcomeMonitor();
        monitor.Record(succeeded: false, DateTimeOffset.UtcNow);
        monitor.Record(succeeded: true, DateTimeOffset.UtcNow);

        // The provider is answering; the failure is about that request, not the service.
        var result = await CheckAsync(monitor);

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }
}
