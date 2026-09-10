using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Campaigns;

/// <summary>
/// The campaign page's offer to file unfiled sources, end to end: the detail carries it for
/// a GM and not for a player, and the confirmation route files what it names.
/// </summary>
[TestFixture]
public class CampaignFilingEndpointTests
{
    private static readonly DateTimeOffset Began = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ended = new(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<Campaign> SeedCampaignAsync(Guid worldId, Guid gmUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = "The Salt Road",
            Status = CampaignStatus.Active,
            StartedAt = Began,
            EndedAt = Ended,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = gmUserId
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    private async Task<Guid?> CampaignOfAsync(Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        return (await db.Sources.AsNoTracking().SingleAsync(s => s.Id == sourceId)).CampaignId;
    }

    [Test]
    public async Task Detail_offers_the_GM_the_unfiled_sources_in_span_and_the_player_nothing()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId);
        var inside = await SourceTestHelpers.CreateTestSourceAsync(_factory, scenario.World.Id, scenario.GmUserId,
            title: "Session 3", processingStatus: SourceProcessingStatus.Processed, occurredAt: Began.AddDays(40));
        await SourceTestHelpers.CreateTestSourceAsync(_factory, scenario.World.Id, scenario.GmUserId,
            title: "Undated lore", processingStatus: SourceProcessingStatus.Processed);

        var url = $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/detail";
        var toGm = await scenario.GmClient.GetFromJsonAsync<CampaignDetailResponse>(url);
        var toPlayer = await scenario.PlayerClient.GetFromJsonAsync<CampaignDetailResponse>(url);

        Assert.Multiple(() =>
        {
            Assert.That(toGm!.UnfiledInSpan.Items.Select(s => s.Id), Is.EqualTo([inside.Id]));
            Assert.That(toGm.UnfiledInSpan.TotalCount, Is.EqualTo(1));
            Assert.That(toPlayer!.UnfiledInSpan.Items, Is.Empty);
            Assert.That(toPlayer.UnfiledInSpan.TotalCount, Is.Zero);
        });
    }

    [Test]
    public async Task Filing_moves_the_named_sources_and_the_offer_empties()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId);
        var source = await SourceTestHelpers.CreateTestSourceAsync(_factory, scenario.World.Id, scenario.GmUserId,
            title: "Session 3", processingStatus: SourceProcessingStatus.Processed, occurredAt: Began.AddDays(40));

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/file-sources",
            new { sourceIds = new[] { source.Id } });
        var filed = await response.Content.ReadFromJsonAsync<FileCampaignSourcesResponse>();
        var detail = await scenario.GmClient.GetFromJsonAsync<CampaignDetailResponse>(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/detail");

        var filedUnder = await CampaignOfAsync(source.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(filed!.FiledCount, Is.EqualTo(1));
            Assert.That(filedUnder, Is.EqualTo(campaign.Id));
            Assert.That(detail!.UnfiledInSpan.TotalCount, Is.Zero);
            Assert.That(detail.SessionCount, Is.EqualTo(1), "the filed source should now be one of the campaign's sessions");
        });
    }

    [Test]
    public async Task Filing_is_refused_to_a_player()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId);
        var source = await SourceTestHelpers.CreateTestSourceAsync(_factory, scenario.World.Id, scenario.PlayerUserId,
            title: "Session 3", processingStatus: SourceProcessingStatus.Processed, occurredAt: Began.AddDays(40));

        var response = await scenario.PlayerClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/file-sources",
            new { sourceIds = new[] { source.Id } });

        var filedUnder = await CampaignOfAsync(source.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(filedUnder, Is.Null);
        });
    }

    [Test]
    public async Task Filing_a_source_already_filed_elsewhere_is_refused_whole()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId);
        var other = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId);
        var theirs = await SourceTestHelpers.CreateTestSourceAsync(_factory, scenario.World.Id, scenario.GmUserId,
            title: "Their session", processingStatus: SourceProcessingStatus.Processed, occurredAt: Began.AddDays(40));
        var free = await SourceTestHelpers.CreateTestSourceAsync(_factory, scenario.World.Id, scenario.GmUserId,
            title: "Session 3", processingStatus: SourceProcessingStatus.Processed, occurredAt: Began.AddDays(41));
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            (await db.Sources.SingleAsync(s => s.Id == theirs.Id)).CampaignId = other.Id;
            await db.SaveChangesAsync();
        }

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/campaigns/{campaign.Id}/file-sources",
            new { sourceIds = new[] { free.Id, theirs.Id } });

        var theirsUnder = await CampaignOfAsync(theirs.Id);
        var freeUnder = await CampaignOfAsync(free.Id);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(theirsUnder, Is.EqualTo(other.Id));
            Assert.That(freeUnder, Is.Null, "part of a refused request was applied");
        });
    }
}
