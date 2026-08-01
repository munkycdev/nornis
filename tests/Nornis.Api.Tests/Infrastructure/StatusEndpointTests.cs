using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// The system-status endpoints, and the separation that is the point of them: /health
/// answers "is this deploy broken" for the availability alert, /status answers "are the
/// dependencies healthy" for the ops page. A dependency failure reaching /health would
/// page someone about a missed migration that never happened.
///
/// Only the two configuration-free checks (worker-heartbeat, azure-openai) register in a
/// test host — sql, blob-storage and service-bus need connection strings the test
/// environment deliberately does not supply.
/// </summary>
[TestFixture]
public class StatusEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task<JsonElement> GetStatusAsync()
    {
        var response = await _client.GetAsync("/status");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static string[] CheckNames(JsonElement status) =>
        status.GetProperty("checks").EnumerateArray()
            .Select(check => check.GetProperty("name").GetString()!)
            .ToArray();

    private void SeedHeartbeat(DateTimeOffset at)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        context.Set<WorkerHeartbeat>().Add(new WorkerHeartbeat
        {
            WorkerName = WorkerHeartbeatHealthCheck.WorkerName,
            BeatAt = at
        });
        context.SaveChanges();
    }

    /// <summary>
    /// Work the worker owes. With no heartbeat alongside it, this is the one combination
    /// the worker-heartbeat check treats as a genuine failure.
    /// </summary>
    private void SeedQueuedSource()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        context.Sources.Add(new Source
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            Title = "Awaiting extraction",
            CreatedAt = DateTimeOffset.UtcNow,
            ProcessingStatus = SourceProcessingStatus.Queued
        });
        context.SaveChanges();
    }

    [Test]
    public async Task Status_IsAnonymous()
    {
        var response = await _client.GetAsync("/status");

        // The API authenticates by default; /status is one of two carve-outs the steering
        // doc allows, so an unauthenticated caller must get a verdict, not a 401.
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Status_ReportsDependencyChecks()
    {
        var names = CheckNames(await GetStatusAsync());

        Assert.That(names, Is.EquivalentTo(new[] { "azure-openai", "worker-heartbeat" }));
    }

    [Test]
    public async Task Status_DoesNotReportTheLivenessCheck()
    {
        var names = CheckNames(await GetStatusAsync());

        // pending-migrations is /health's business. Duplicating it here would mean a
        // missed migration showing up as a dependency problem.
        Assert.That(names, Does.Not.Contain("pending-migrations"));
    }

    [Test]
    public async Task Status_CarriesNameStatusAndDurationOnly()
    {
        var status = await GetStatusAsync();
        var check = status.GetProperty("checks").EnumerateArray().First();

        // The payload is public. Descriptions and exception text are where connection
        // strings and hostnames leak, so the shape itself is the guard.
        Assert.That(
            check.EnumerateObject().Select(p => p.Name),
            Is.EquivalentTo(new[] { "name", "status", "durationMs" }));
    }

    [Test]
    public async Task Status_WithAFreshHeartbeat_IsHealthy()
    {
        SeedHeartbeat(DateTimeOffset.UtcNow);

        var response = await _client.GetAsync("/status");
        var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(status.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task Status_OnAnIdleSystemWithNoWorker_IsHealthy()
    {
        // Nothing queued and no heartbeat ever written — which is exactly how production
        // looks most of the time, because the worker scales to zero when the queue drains.
        // This shipped as a 503 for its first hour; it is the reason the check now asks
        // whether work is outstanding before it asks whether the worker is awake.
        var response = await _client.GetAsync("/status");
        var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(status.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
        });
    }

    [Test]
    public async Task Status_WithWorkQueuedAndNoWorker_IsUnhealthy()
    {
        SeedQueuedSource();

        var response = await _client.GetAsync("/status");
        var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(status.GetProperty("status").GetString(), Is.EqualTo("Unhealthy"));
        });
    }

    [Test]
    public async Task Health_StaysGreenWhenADependencyIsDown()
    {
        // Work outstanding and no worker to drain it puts /status at Unhealthy — and
        // /health must not care. This is the regression that would re-fuse the two signals.
        SeedQueuedSource();

        var statusResponse = await _client.GetAsync("/status");
        var healthResponse = await _client.GetAsync("/health");

        Assert.Multiple(() =>
        {
            Assert.That(statusResponse.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(healthResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Health_KeepsItsOriginalShape()
    {
        var response = await _client.GetAsync("/health");
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        // The availability alert reads this. Adding /status must not have changed it.
        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("status").GetString(), Is.EqualTo("Healthy"));
            Assert.That(body.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(new[] { "status" }));
        });
    }
}
