using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

/// <summary>
/// Deleting a campaign detaches its sources rather than taking them with it — the database
/// deliberately does not cascade there, so the repository does it in four set-based writes.
///
/// The reason this fixture exists at all: those four calls were unguarded `ExecuteUpdate` /
/// `ExecuteDelete`, which the InMemory provider these tests run on cannot execute. No test
/// reached them, so the landmine sat there looking exactly like the guarded calls elsewhere.
/// This is the test that reaches them.
/// </summary>
[TestFixture]
public class CampaignDeletionTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Campaign> SeedCampaignAsync(Guid worldId, Guid createdByUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = "The Vespergale Reach",
            Status = CampaignStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = createdByUserId
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    [Test]
    public async Task Delete_DetachesItsSourcesAndRemovesTheCampaign()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId, title: "Session 5");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var tracked = await db.Sources.FirstAsync(s => s.Id == source.Id);
            tracked.CampaignId = campaign.Id;
            await db.SaveChangesAsync();
        }

        var response = await scenario.GmClient.DeleteAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}");

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<NornisDbContext>();

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await verifyDb.Campaigns.AnyAsync(c => c.Id == campaign.Id), Is.False);

            // The source outlives the campaign it was filed under. Losing session notes
            // because a GM tidied up their campaign list would be unrecoverable.
            var survivor = await verifyDb.Sources.FirstOrDefaultAsync(s => s.Id == source.Id);
            Assert.That(survivor, Is.Not.Null);
            Assert.That(survivor!.CampaignId, Is.Null);
        });
    }
}
