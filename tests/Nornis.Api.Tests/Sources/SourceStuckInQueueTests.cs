using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Application.Services;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

/// <summary>
/// The Queued wedge's exit has to reach the page. Mark-ready has retried a source stuck at
/// Queued for over an hour since 2026-08-02, but the source page offered Process only for Draft,
/// Ready and Failed, so a wedged source showed no button at all. <c>StuckInQueue</c> is how the
/// Web learns the server's verdict instead of keeping its own copy of the hour.
///
/// Both responses are checked because both are read: the source page reads the detail, the
/// processing queue panel reads the list — and the list is projected separately in the
/// repository, so it can drop the timestamp the rule needs without the detail noticing.
/// </summary>
[TestFixture]
public class SourceStuckInQueueTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _scenario.ObserverClient.Dispose();
        _factory.Dispose();
    }

    private async Task<Guid> SeedQueuedAsync(TimeSpan queuedFor)
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            processingStatus: SourceProcessingStatus.Queued,
            body: "We questioned Captain Voss.");

        // The DbContext stamps StatusChangedAt on insert; a second save that leaves the status
        // alone keeps whatever is written here, which is how a source ages in place.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var row = await db.Sources.FindAsync(source.Id);
        row!.StatusChangedAt = DateTimeOffset.UtcNow - queuedFor;
        await db.SaveChangesAsync();
        return source.Id;
    }

    private async Task<(bool Detail, bool List)> ReadStuckAsync(Guid sourceId)
    {
        var detail = await _scenario.GmClient.GetFromJsonAsync<SourceResponse>(
            $"/api/worlds/{_scenario.World.Id}/sources/{sourceId}");
        var list = await _scenario.GmClient.GetFromJsonAsync<List<SourceListItemResponse>>(
            $"/api/worlds/{_scenario.World.Id}/sources");

        return (detail!.StuckInQueue, list!.Single(s => s.Id == sourceId).StuckInQueue);
    }

    [Test]
    public async Task QueuedPastTheThreshold_IsReportedStuck_OnDetailAndList()
    {
        var id = await SeedQueuedAsync(SourceService.StaleQueuedThreshold + TimeSpan.FromMinutes(5));

        var (detail, list) = await ReadStuckAsync(id);

        Assert.Multiple(() =>
        {
            Assert.That(detail, Is.True, "the source page would show no Retry");
            Assert.That(list, Is.True, "the processing queue would show no Resubmit");
        });
    }

    [Test]
    public async Task RecentlyQueued_IsNotReportedStuck()
    {
        var id = await SeedQueuedAsync(TimeSpan.FromMinutes(5));

        var (detail, list) = await ReadStuckAsync(id);

        // Five minutes in, a retry would race a live delivery and pay twice.
        Assert.Multiple(() =>
        {
            Assert.That(detail, Is.False);
            Assert.That(list, Is.False);
        });
    }

    [Test]
    public async Task TheButtonTheResponseOffers_Works()
    {
        var id = await SeedQueuedAsync(SourceService.StaleQueuedThreshold + TimeSpan.FromMinutes(5));

        var response = await _scenario.GmClient.PostAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{id}/ready", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var retried = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(retried!.ProcessingStatus, Is.EqualTo("Queued"));
            Assert.That(retried.StuckInQueue, Is.False, "a fresh enqueue restarts the hour");
        });
    }
}
