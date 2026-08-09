using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Campaigns;

/// <summary>
/// Choosing the campaign a world is playing now, end to end.
///
/// Routing is the thing only this level catches: <c>DELETE /campaigns/current</c> sits beside
/// <c>DELETE /campaigns/{campaignId:guid}</c> and the two are told apart by the guid
/// constraint alone — drop it and clearing the pointer starts binding as a campaign delete,
/// which would destroy a campaign a GM only meant to stop playing.
/// </summary>
[TestFixture]
public class CurrentCampaignEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Campaign> SeedCampaignAsync(
        Guid worldId, string name, CampaignStatus status = CampaignStatus.Active)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = name,
            Status = status,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = Guid.NewGuid()
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    private async Task<Guid?> ReadCurrentAsync(Guid worldId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        return (await db.Worlds.FindAsync(worldId))!.CurrentCampaignId;
    }

    [Test]
    public async Task AGm_CanChooseTheCurrentCampaign()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "Missing Caravan Arc");

        var response = await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/current", new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            await response.Content.ReadAsStringAsync());
        Assert.That(await ReadCurrentAsync(scenario.World.Id), Is.EqualTo(campaign.Id));
    }

    [Test]
    public async Task TheWorldPayload_CarriesIt_SoTheCaptureFormNeedsNoExtraCall()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "Missing Caravan Arc");
        await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/current", new { });

        var worlds = await scenario.PlayerClient.GetFromJsonAsync<List<WorldListItemResponse>>("/api/worlds");

        var world = worlds!.Single(w => w.Id == scenario.World.Id);
        Assert.That(world.CurrentCampaignId, Is.EqualTo(campaign.Id),
            "a player capturing notes files them into the campaign the table is playing");
    }

    [Test]
    public async Task APlayer_CannotChooseIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "Missing Caravan Arc");

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/current", new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await ReadCurrentAsync(scenario.World.Id), Is.Null);
    }

    [Test]
    public async Task ACampaignThatIsNotActive_IsRefused()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var finished = await SeedCampaignAsync(scenario.World.Id, "The Silver Key", CampaignStatus.Completed);

        var response = await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{finished.Id}/current", new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("campaign_not_active"));
    }

    [Test]
    public async Task ACampaignInAnotherWorld_IsNotFound()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var elsewhere = await SeedCampaignAsync(Guid.NewGuid(), "Someone else's game");

        var response = await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{elsewhere.Id}/current", new { });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Clearing_LeavesTheCampaignItself_Alone()
    {
        // The routing hazard: "current" must not bind as a campaign id and delete the campaign.
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "Missing Caravan Arc");
        await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/current", new { });

        var response = await scenario.GmClient.DeleteAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/current");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await ReadCurrentAsync(scenario.World.Id), Is.Null);

        var campaigns = await scenario.GmClient.GetFromJsonAsync<List<CampaignResponse>>(
            $"/api/worlds/{scenario.World.Id}/campaigns");
        Assert.That(campaigns!.Any(c => c.Id == campaign.Id), Is.True,
            "stopping play is not deleting the campaign");
    }

    [Test]
    public async Task APlayer_CannotClearIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "Missing Caravan Arc");
        await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/current", new { });

        var response = await scenario.PlayerClient.DeleteAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/current");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(await ReadCurrentAsync(scenario.World.Id), Is.EqualTo(campaign.Id));
    }
}
