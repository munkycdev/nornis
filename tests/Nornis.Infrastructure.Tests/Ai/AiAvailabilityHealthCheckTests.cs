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
        new AiAvailabilityHealthCheck(monitor, PauseGate(paused: false)).CheckHealthAsync(new HealthCheckContext());

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

    private static IAiPauseGate PauseGate(bool paused, string? reason = null) =>
        new StubPauseGate(paused ? new AiPauseState(true, reason) : AiPauseState.Running);

    private sealed class StubPauseGate : IAiPauseGate
    {
        private readonly AiPauseState _state;
        public StubPauseGate(AiPauseState state) => _state = state;
        public Task<AiPauseState> GetAsync(CancellationToken ct) => Task.FromResult(_state);
    }

    [Test]
    public async Task WhenAiIsPaused_TheCheckSaysWhoTurnedItOff()
    {
        var monitor = new AiOutcomeMonitor();
        var result = await new AiAvailabilityHealthCheck(monitor, PauseGate(true, "Provider incident"))
            .CheckHealthAsync(new HealthCheckContext());

        // Degraded, because the feature genuinely is unavailable — but the text has to
        // distinguish "we did this on purpose" from "Azure is broken", which is the whole
        // difference between a calm page and a false alarm.
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
            Assert.That(result.Description, Does.Contain("paused by an operator"));
            Assert.That(result.Description, Does.Contain("Provider incident"));
        });
    }
}
