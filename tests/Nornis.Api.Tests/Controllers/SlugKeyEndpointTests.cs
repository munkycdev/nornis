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

namespace Nornis.Api.Tests.Controllers;

/// <summary>
/// Detail pages are addressed by slug, and every link that predates slugs carries an id. Both
/// keys must open the same door: the whole response is compared, not a field, so a projection
/// that forgets the slug or answers the two keys differently fails here rather than in a page.
/// Unknown slugs and unknown ids must also be the same 404, or the slug path becomes a cheaper
/// existence oracle than the id path the security notes already close.
/// </summary>
[TestFixture]
public class SlugKeyEndpointTests
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
    public void TearDown() => _factory.Dispose();

    private Guid WorldId => _scenario.World.Id;

    private async Task<T> SeedAsync<T>(T entity) where T : class
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        db.Add(entity);
        await db.SaveChangesAsync();
        return entity;
    }

    private Task<Campaign> SeedCampaignAsync(string name) => SeedAsync(new Campaign
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Name = name,
        Status = CampaignStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = _scenario.GmUserId
    });

    private async Task<Character> SeedCharacterAsync(string name)
    {
        var player = await SeedAsync(new Player
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = "Henry",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return await SeedAsync(new Character
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            PlayerId = player.Id,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    private Task<LibraryDocument> SeedDocumentAsync(string title) => SeedAsync(new LibraryDocument
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        Title = title,
        FileName = "guide.pdf",
        ContentType = "application/pdf",
        BlobPath = $"worlds/{WorldId}/library/guide.pdf",
        Kind = LibraryDocumentKind.Sourcebook,
        Visibility = VisibilityScope.PartyVisible,
        Status = LibraryDocumentStatus.Indexed,
        UploadedByUserId = _scenario.GmUserId,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    });

    private static async Task AssertSameDoor(HttpClient client, string bySlug, string byId)
    {
        var slugResponse = await client.GetAsync(bySlug);
        var idResponse = await client.GetAsync(byId);
        var slugBody = await slugResponse.Content.ReadAsStringAsync();
        var idBody = await idResponse.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(slugResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), slugBody);
            Assert.That(idResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), idBody);
            Assert.That(slugBody, Is.EqualTo(idBody), "the slug and the id must open the same page");
        });
    }

    private static async Task AssertSame404(HttpClient client, string bySlug, string byId)
    {
        var slugResponse = await client.GetAsync(bySlug);
        var idResponse = await client.GetAsync(byId);

        await Assert.MultipleAsync(async () =>
        {
            Assert.That(slugResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(idResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(await slugResponse.Content.ReadAsStringAsync(),
                Is.EqualTo(await idResponse.Content.ReadAsStringAsync()),
                "an unknown slug and an unknown id must be indistinguishable");
        });
    }

    #region Member endpoints

    [Test]
    public async Task Artifact_BySlug_IsTheSamePageAsById()
    {
        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, WorldId, "Captain Voss");
        Assert.That(voss.Slug, Is.EqualTo("captain-voss"), "seeding through the context assigns the slug");

        await AssertSameDoor(_scenario.PlayerClient,
            $"/api/worlds/{WorldId}/artifacts/captain-voss",
            $"/api/worlds/{WorldId}/artifacts/{voss.Id}");
    }

    [Test]
    public async Task Artifact_ResponsesCarryTheSlug_InDetailAndList()
    {
        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, WorldId, "Captain Voss");

        var detail = await _scenario.GmClient.GetFromJsonAsync<ArtifactDetailResponse>(
            $"/api/worlds/{WorldId}/artifacts/{voss.Id}");
        var list = await _scenario.GmClient.GetFromJsonAsync<List<ArtifactListItemResponse>>(
            $"/api/worlds/{WorldId}/artifacts");

        Assert.Multiple(() =>
        {
            Assert.That(detail!.Slug, Is.EqualTo("captain-voss"));
            Assert.That(list!.Single().Slug, Is.EqualTo("captain-voss"));
        });
    }

    [Test]
    public async Task Artifact_UnknownSlug_IsTheSame404AsUnknownId()
    {
        await AssertSame404(_scenario.GmClient,
            $"/api/worlds/{WorldId}/artifacts/nobody-here",
            $"/api/worlds/{WorldId}/artifacts/{Guid.NewGuid()}");
    }

    [Test]
    public async Task Artifact_AnotherWorldsSlug_IsNotFoundHere()
    {
        var elsewhere = await SourceTestHelpers.CreateTestWorldAsync(_factory, _scenario.GmUserId);
        await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, elsewhere.Id, "Captain Voss");

        var response = await _scenario.GmClient.GetAsync($"/api/worlds/{WorldId}/artifacts/captain-voss");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Artifact_LiteralRoutes_StillWinOverTheKey()
    {
        // "graph" and "search" are sibling routes; a key parameter must not swallow them.
        var graph = await _scenario.GmClient.GetAsync($"/api/worlds/{WorldId}/artifacts/graph");
        var search = await _scenario.GmClient.GetAsync($"/api/worlds/{WorldId}/artifacts/search?q=voss");

        Assert.Multiple(() =>
        {
            Assert.That(graph.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(search.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task Source_BySlug_IsTheSamePageAsById_AndCarriesCampaignSlug()
    {
        var campaign = await SeedCampaignAsync("The Missing Caravan");
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, WorldId, _scenario.GmUserId, title: "Session 4: Questioning Voss");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            db.Sources.Single(s => s.Id == source.Id).CampaignId = campaign.Id;
            await db.SaveChangesAsync();
        }

        await AssertSameDoor(_scenario.GmClient,
            $"/api/worlds/{WorldId}/sources/session-4-questioning-voss",
            $"/api/worlds/{WorldId}/sources/{source.Id}");

        var detail = await _scenario.GmClient.GetFromJsonAsync<SourceResponse>(
            $"/api/worlds/{WorldId}/sources/session-4-questioning-voss");
        var list = await _scenario.GmClient.GetFromJsonAsync<List<SourceListItemResponse>>(
            $"/api/worlds/{WorldId}/sources");
        Assert.Multiple(() =>
        {
            Assert.That(detail!.Slug, Is.EqualTo("session-4-questioning-voss"));
            Assert.That(detail.CampaignSlug, Is.EqualTo("the-missing-caravan"));
            Assert.That(list!.Single(s => s.Id == source.Id).CampaignSlug, Is.EqualTo("the-missing-caravan"));
        });
    }

    [Test]
    public async Task Source_LiteralRoute_StillWinsOverTheKey()
    {
        var activity = await _scenario.GmClient.GetAsync($"/api/worlds/{WorldId}/sources/activity");
        Assert.That(activity.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Campaign_BySlug_IsTheSamePageAsById()
    {
        var campaign = await SeedCampaignAsync("The Missing Caravan");

        await AssertSameDoor(_scenario.PlayerClient,
            $"/api/worlds/{WorldId}/campaigns/the-missing-caravan/detail",
            $"/api/worlds/{WorldId}/campaigns/{campaign.Id}/detail");
        await AssertSame404(_scenario.PlayerClient,
            $"/api/worlds/{WorldId}/campaigns/no-such-run/detail",
            $"/api/worlds/{WorldId}/campaigns/{Guid.NewGuid()}/detail");

        var list = await _scenario.GmClient.GetFromJsonAsync<List<CampaignResponse>>($"/api/worlds/{WorldId}/campaigns");
        Assert.That(list!.Single().Slug, Is.EqualTo("the-missing-caravan"));
    }

    [Test]
    public async Task Character_BySlug_IsTheSamePageAsById()
    {
        var character = await SeedCharacterAsync("Mira Voss");

        await AssertSameDoor(_scenario.GmClient,
            $"/api/worlds/{WorldId}/characters/mira-voss/dossier",
            $"/api/worlds/{WorldId}/characters/{character.Id}/dossier");
        await AssertSame404(_scenario.GmClient,
            $"/api/worlds/{WorldId}/characters/nobody/dossier",
            $"/api/worlds/{WorldId}/characters/{Guid.NewGuid()}/dossier");

        var dossier = await _scenario.GmClient.GetFromJsonAsync<CharacterDossierResponse>(
            $"/api/worlds/{WorldId}/characters/mira-voss/dossier");
        Assert.That(dossier!.Character.Slug, Is.EqualTo("mira-voss"));
    }

    [Test]
    public async Task LibraryDocument_BySlug_IsTheSamePageAsById()
    {
        var document = await SeedDocumentAsync("Player's Guide");
        Assert.That(document.Slug, Is.EqualTo("players-guide"));

        await AssertSameDoor(_scenario.GmClient,
            $"/api/worlds/{WorldId}/library/players-guide",
            $"/api/worlds/{WorldId}/library/{document.Id}");
        await AssertSame404(_scenario.GmClient,
            $"/api/worlds/{WorldId}/library/no-such-book",
            $"/api/worlds/{WorldId}/library/{Guid.NewGuid()}");
    }

    [Test]
    public async Task Jump_CarriesSlugs_ForEveryKind()
    {
        await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, WorldId, "Captain Voss");
        await SeedCampaignAsync("The Missing Caravan");

        var byVoss = await _scenario.GmClient.GetFromJsonAsync<JumpResponse>($"/api/worlds/{WorldId}/jump?q=voss");
        var byCaravan = await _scenario.GmClient.GetFromJsonAsync<JumpResponse>($"/api/worlds/{WorldId}/jump?q=caravan");

        Assert.Multiple(() =>
        {
            Assert.That(byVoss!.Groups.Single(g => g.Kind == "Artifact").Items.Select(i => i.Slug), Is.EqualTo(["captain-voss"]));
            Assert.That(byCaravan!.Groups.Single(g => g.Kind == "Campaign").Items.Select(i => i.Slug), Is.EqualTo(["the-missing-caravan"]));
        });
    }

    #endregion

    #region Public endpoints

    private async Task PublishAsync(string slug = "black-harbor")
    {
        var update = await _scenario.GmClient.PutAsJsonAsync($"/api/worlds/{WorldId}",
            new UpdateWorldRequest(PublicSlug: slug, PublicAccessEnabled: true));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task PublicArtifact_BySlug_IsTheSamePageAsById()
    {
        await PublishAsync();
        var voss = await KnowledgeTestHelpers.CreateTestArtifactAsync(_factory, WorldId, "Captain Voss");
        using var anonymous = _factory.CreateClient();

        await AssertSameDoor(anonymous,
            "/api/public/worlds/black-harbor/artifacts/captain-voss",
            $"/api/public/worlds/black-harbor/artifacts/{voss.Id}");
        await AssertSame404(anonymous,
            "/api/public/worlds/black-harbor/artifacts/nobody-here",
            $"/api/public/worlds/black-harbor/artifacts/{Guid.NewGuid()}");
    }

    [Test]
    public async Task PublicArtifact_GmOnlySlug_IsTheSame404AsAnUnknownOne()
    {
        await PublishAsync();
        await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, WorldId, "Hidden Ledger", visibility: VisibilityScope.GMOnly);
        using var anonymous = _factory.CreateClient();

        await AssertSame404(anonymous,
            "/api/public/worlds/black-harbor/artifacts/hidden-ledger",
            "/api/public/worlds/black-harbor/artifacts/nobody-here");
    }

    [Test]
    public async Task PublicSource_BySlug_IsTheSamePageAsById()
    {
        await PublishAsync();
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, WorldId, _scenario.GmUserId, title: "Session 4",
            processingStatus: SourceProcessingStatus.Processed);
        using var anonymous = _factory.CreateClient();

        await AssertSameDoor(anonymous,
            "/api/public/worlds/black-harbor/sources/session-4",
            $"/api/public/worlds/black-harbor/sources/{source.Id}");
    }

    [Test]
    public async Task PublicCampaign_BySlug_IsTheSamePageAsById()
    {
        await PublishAsync();
        var campaign = await SeedCampaignAsync("The Missing Caravan");
        using var anonymous = _factory.CreateClient();

        await AssertSameDoor(anonymous,
            "/api/public/worlds/black-harbor/campaigns/the-missing-caravan/detail",
            $"/api/public/worlds/black-harbor/campaigns/{campaign.Id}/detail");
    }

    #endregion
}
