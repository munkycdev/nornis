using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Storylines;

[TestFixture]
public class RelationshipBackfillEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task Backfill_AsPlayer_Returns403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.PlayerClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/storylines/backfill-relationships", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Backfill_QueuesProcessedSources_AndSkipsSweptOnes()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var fresh = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            title: "Old session", processingStatus: SourceProcessingStatus.Processed,
            body: "The heist advanced the investigation.");

        var swept = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            title: "Already swept session", processingStatus: SourceProcessingStatus.Processed,
            body: "Old news.");

        await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            title: "Draft note", processingStatus: SourceProcessingStatus.Draft,
            body: "Not extracted yet.");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            db.ReviewBatches.Add(new ReviewBatch
            {
                Id = Guid.NewGuid(),
                WorldId = scenario.World.Id,
                SourceId = swept.Id,
                Kind = "RelationshipBackfill",
                Status = ReviewBatchStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/storylines/backfill-relationships", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<BackfillQueueResponse>();
        Assert.That(result!.QueuedCount, Is.EqualTo(1));
        Assert.That(result.AlreadySweptCount, Is.EqualTo(1));
        Assert.That(result.TotalEligible, Is.EqualTo(2));
        Assert.That(fresh.Id, Is.Not.EqualTo(swept.Id));

        // Re-running immediately queues the fresh source again (no batch exists yet —
        // the worker isn't running in this factory), proving the endpoint itself is
        // stateless and idempotency lives on the batch.
        var again = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/storylines/backfill-relationships", null);
        var againResult = await again.Content.ReadFromJsonAsync<BackfillQueueResponse>();
        Assert.That(againResult!.QueuedCount, Is.EqualTo(1));
    }
}
