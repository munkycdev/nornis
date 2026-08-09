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
/// The campaign page's endpoints, end to end.
///
/// Routing is the thing only this level catches: <c>PUT /campaigns/reorder</c> sits beside
/// <c>PUT /campaigns/{campaignId:guid}</c>, and the two are told apart by the guid
/// constraint alone. Drop that constraint and "reorder" starts binding as a campaign id.
/// </summary>
[TestFixture]
public class CampaignPageEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Campaign> SeedCampaignAsync(Guid worldId, string name, int sortOrder = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = name,
            Status = CampaignStatus.Active,
            SortOrder = sortOrder,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = Guid.NewGuid()
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    #region Detail

    [Test]
    public async Task Detail_is_served_to_a_player()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "The Missing Caravan");

        var response = await scenario.PlayerClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/detail");

        var detail = await response.Content.ReadFromJsonAsync<CampaignDetailResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(detail!.Campaign.Name, Is.EqualTo("The Missing Caravan"));
        });
    }

    [Test]
    public async Task Detail_of_another_worlds_campaign_is_not_found()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var other = await SeedCampaignAsync(Guid.NewGuid(), "Another World's Campaign");

        var response = await scenario.GmClient.GetAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{other.Id}/detail");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    #endregion

    #region Reorder

    [Test]
    public async Task Reorder_route_is_not_swallowed_by_the_campaign_id_route()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var first = await SeedCampaignAsync(scenario.World.Id, "The Original Campaign");
        var second = await SeedCampaignAsync(scenario.World.Id, "The Sequel");

        var response = await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/reorder",
            new { campaignIds = new[] { second.Id, first.Id } });

        var reordered = await response.Content.ReadFromJsonAsync<List<CampaignResponse>>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(reordered!.Select(c => c.Name),
                Is.EqualTo(["The Sequel", "The Original Campaign"]));
        });
    }

    [Test]
    public async Task Reorder_survives_a_round_trip_through_the_list_endpoint()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var first = await SeedCampaignAsync(scenario.World.Id, "The Original Campaign");
        var second = await SeedCampaignAsync(scenario.World.Id, "The Sequel");
        var third = await SeedCampaignAsync(scenario.World.Id, "The Side Game");

        await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/reorder",
            new { campaignIds = new[] { third.Id, first.Id, second.Id } });

        var listed = await scenario.GmClient.GetFromJsonAsync<List<CampaignResponse>>(
            $"/api/worlds/{scenario.World.Id}/campaigns");

        Assert.That(listed!.Select(c => c.Name),
            Is.EqualTo(["The Side Game", "The Original Campaign", "The Sequel"]));
    }

    [Test]
    public async Task Reorder_is_refused_to_a_player()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "The Missing Caravan");

        var response = await scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/reorder",
            new { campaignIds = new[] { campaign.Id } });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region Recap

    [Test]
    public async Task Recap_generation_is_refused_to_a_player()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, "The Missing Caravan");

        var response = await scenario.PlayerClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/recap", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion
}
