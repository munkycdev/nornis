using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Public;

/// <summary>
/// The public campaign pages: what a stranger sees of a run of play. The tests that matter
/// are the ones about what is absent — the GM rendering of the recap, a GM-only entry the
/// campaign's sources cite, a reveal filed under it.
/// </summary>
[TestFixture]
public class PublicCampaignEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private async Task<SourceTestScenario> SetupPublicWorldAsync(bool enabled = true)
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var update = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}",
            new UpdateWorldRequest(PublicSlug: "black-harbor", PublicAccessEnabled: enabled));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
        return scenario;
    }

    private async Task<Campaign> SeedCampaignAsync(Guid worldId, Guid gmUserId, string name)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var campaign = new Campaign
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Name = name,
            Description = "The caravan never arrived.",
            Status = CampaignStatus.Active,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = gmUserId,
        };
        db.Campaigns.Add(campaign);
        db.CampaignRecaps.Add(new CampaignRecap
        {
            Id = Guid.NewGuid(),
            CampaignId = campaign.Id,
            GmContentMarkdown = "GM SECRET: the mayor did it.",
            PartyContentMarkdown = "The party has reached Black Harbor.",
            Model = "test",
            GeneratedAt = now,
            GeneratedByUserId = gmUserId,
        });
        await db.SaveChangesAsync();
        return campaign;
    }

    /// <summary>Henry, not on Nornis, plays Malliano in this campaign.</summary>
    private async Task SeedCastAsync(Guid worldId, Guid campaignId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var henry = new Player { Id = Guid.NewGuid(), WorldId = worldId, Name = "Henry", CreatedAt = now, UpdatedAt = now };
        var malliano = new Character { Id = Guid.NewGuid(), WorldId = worldId, PlayerId = henry.Id, Name = "Malliano", CreatedAt = now, UpdatedAt = now };
        db.Players.Add(henry);
        db.Characters.Add(malliano);
        db.CampaignCharacters.Add(new CampaignCharacter { Id = Guid.NewGuid(), CampaignId = campaignId, CharacterId = malliano.Id, CreatedAt = now });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedSourceAsync(Guid worldId, Guid gmUserId, Guid campaignId, SourceType type, string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            CampaignId = campaignId,
            Type = type,
            Title = title,
            Body = "Text.",
            OccurredAt = now,
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = now,
            CreatedByUserId = gmUserId,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source.Id;
    }

    [Test]
    public async Task Campaigns_ListedForAPublicWorld_404ForADisabledOne()
    {
        var scenario = await SetupPublicWorldAsync();
        await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId, "The Missing Caravan");

        var list = await _anonymous.GetFromJsonAsync<List<CampaignResponse>>("/api/public/worlds/black-harbor/campaigns");
        Assert.That(list!.Select(c => c.Name), Is.EqualTo(new List<string> { "The Missing Caravan" }));

        await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}", new UpdateWorldRequest(PublicAccessEnabled: false));
        var disabled = await _anonymous.GetAsync("/api/public/worlds/black-harbor/campaigns");
        Assert.That(disabled.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    [Category("Authorization")]
    public async Task CampaignDetail_CarriesThePartyRecap_AndNeverTheGmOne()
    {
        var scenario = await SetupPublicWorldAsync();
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId, "The Missing Caravan");

        var response = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/campaigns/{campaign.Id}/detail");
        var raw = await response.Content.ReadAsStringAsync();
        var detail = await response.Content.ReadFromJsonAsync<PublicCampaignDetailResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), raw);
            Assert.That(detail!.Recap.HasData, Is.True);
            Assert.That(detail.Recap.Content, Is.EqualTo("The party has reached Black Harbor."));
            Assert.That(raw, Does.Not.Contain("GM SECRET"), "the GM rendering has no field to travel in");
            Assert.That(raw, Does.Not.Contain("unfiledInSpan").IgnoreCase);
            Assert.That(detail.Campaign.Description, Is.EqualTo("The caravan never arrived."));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task CampaignDetail_ShowsPartyEntries_NotGmOnes_AndNoReveals()
    {
        var scenario = await SetupPublicWorldAsync();
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId, "The Missing Caravan");
        var session = await SeedSourceAsync(scenario.World.Id, scenario.GmUserId, campaign.Id, SourceType.SessionNote, "Session 1");
        var reveal = await SeedSourceAsync(scenario.World.Id, scenario.GmUserId, campaign.Id, SourceType.Reveal, "Revealed: the mayor");

        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, scenario.World.Id, "Captain Voss");
        var ledger = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Hidden Ledger", visibility: VisibilityScope.GMOnly);
        await KnowledgeTestHelpers.CreateTestSourceReferenceAsync(_factory, session, SourceReferenceTargetType.Artifact, voss.Id);
        await KnowledgeTestHelpers.CreateTestSourceReferenceAsync(_factory, session, SourceReferenceTargetType.Artifact, ledger.Id);

        var detail = await _anonymous.GetFromJsonAsync<PublicCampaignDetailResponse>(
            $"/api/public/worlds/black-harbor/campaigns/{campaign.Id}/detail");

        Assert.Multiple(() =>
        {
            Assert.That(detail!.Artifacts.Select(a => a.Name), Does.Contain("Captain Voss"));
            Assert.That(detail.Artifacts.Select(a => a.Name), Does.Not.Contain("Hidden Ledger"));
            Assert.That(detail.RecentSessions.Select(s => s.Id), Does.Contain(session));
            Assert.That(detail.RecentSessions.Select(s => s.Id), Does.Not.Contain(reveal), "a reveal is addressed to the table");
        });
    }

    [Test]
    public async Task CampaignDetail_NamesTheCastAndWhoPlaysThem()
    {
        var scenario = await SetupPublicWorldAsync();
        var campaign = await SeedCampaignAsync(scenario.World.Id, scenario.GmUserId, "The Missing Caravan");
        await SeedCastAsync(scenario.World.Id, campaign.Id);

        var detail = await _anonymous.GetFromJsonAsync<PublicCampaignDetailResponse>(
            $"/api/public/worlds/black-harbor/campaigns/{campaign.Id}/detail");

        Assert.That(detail!.Cast, Has.Count.EqualTo(1));
        Assert.That(detail.Cast[0].Name, Is.EqualTo("Malliano"));
        Assert.That(detail.Cast[0].PlayerName, Is.EqualTo("Henry"));
    }

    [Test]
    public async Task CampaignDetail_UnknownCampaign_404()
    {
        await SetupPublicWorldAsync();

        var response = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/campaigns/{Guid.NewGuid()}/detail");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
