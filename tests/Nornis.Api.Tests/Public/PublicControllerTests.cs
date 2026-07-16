using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Public;

[TestFixture]
public class PublicControllerTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        // No Authorization header — exercises [AllowAnonymous] against the real FallbackPolicy.
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private async Task<SourceTestScenario> SetupPublicWorldAsync(string slug = "black-harbor", bool enabled = true)
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var update = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{scenario.World.Id}",
            new UpdateWorldRequest(PublicSlug: slug, PublicAccessEnabled: enabled));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
        return scenario;
    }

    [Test]
    public async Task WorldList_CarriesPublicFields_ForSettingsPanelHydration()
    {
        var scenario = await SetupPublicWorldAsync(slug: "black-harbor", enabled: true);

        var worlds = await scenario.GmClient.GetFromJsonAsync<List<WorldListItemResponse>>("/api/worlds");
        var world = worlds!.Single(w => w.Id == scenario.World.Id);

        Assert.That(world.PublicSlug, Is.EqualTo("black-harbor"));
        Assert.That(world.PublicAccessEnabled, Is.True);
    }

    [Test]
    public async Task UnknownSlug_And_DisabledWorld_ReturnIdentical404s()
    {
        await SetupPublicWorldAsync(slug: "black-harbor", enabled: false);

        var unknown = await _anonymous.GetAsync("/api/public/worlds/no-such-world");
        var disabled = await _anonymous.GetAsync("/api/public/worlds/black-harbor");

        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(disabled.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await unknown.Content.ReadAsStringAsync(), Is.EqualTo(await disabled.Content.ReadAsStringAsync()),
            "unknown and disabled must be indistinguishable — no existence oracle");
    }

    [Test]
    public async Task EnabledWorld_ReturnsPublicCardOnly()
    {
        await SetupPublicWorldAsync();

        var response = await _anonymous.GetAsync("/api/public/worlds/black-harbor");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("name"));
        Assert.That(body.ToLowerInvariant(), Does.Not.Contain("budget"), "no budget or member data in public DTOs");
    }

    [Test]
    public async Task PublicArtifacts_ExcludeGmOnly_AndGmOnlyDetail404s()
    {
        var scenario = await SetupPublicWorldAsync();
        var visible = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Captain Voss", visibility: VisibilityScope.PartyVisible);
        var hidden = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Hidden Ledger", visibility: VisibilityScope.GMOnly);

        var list = await _anonymous.GetFromJsonAsync<List<ArtifactListItemResponse>>(
            "/api/public/worlds/black-harbor/artifacts");
        var hiddenDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/artifacts/{hidden.Id}");
        var visibleDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/artifacts/{visible.Id}");

        Assert.That(list!.Select(a => a.Id), Is.EquivalentTo(new[] { visible.Id }));
        Assert.That(hiddenDetail.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(visibleDetail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task PublicArtifactGraph_ExcludesGmOnlyNodes()
    {
        var scenario = await SetupPublicWorldAsync();
        var visible = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Captain Voss", visibility: VisibilityScope.PartyVisible);
        var hidden = await KnowledgeTestHelpers.CreateTestArtifactAsync(
            _factory, scenario.World.Id, "Hidden Ledger", visibility: VisibilityScope.GMOnly);

        var graph = await _anonymous.GetFromJsonAsync<ArtifactGraphResponse>(
            "/api/public/worlds/black-harbor/artifacts/graph");

        Assert.That(graph!.Nodes.Select(n => n.Id), Does.Contain(visible.Id));
        Assert.That(graph.Nodes.Select(n => n.Id), Does.Not.Contain(hidden.Id));
    }

    [Test]
    public async Task PublicSources_ListLimitedToSessionAndImportedNotes_DetailStillReachable()
    {
        var scenario = await SetupPublicWorldAsync();

        var session = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/sources",
            new CreateSourceRequest("Session 1", "SessionNote", "PartyVisible", Body: "We sailed."));
        var sessionId = (await session.Content.ReadFromJsonAsync<SourceResponse>())!.Id;

        var imported = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/sources",
            new CreateSourceRequest("Old campaign log", "ImportedNote", "PartyVisible", Body: "Year one."));
        var importedId = (await imported.Content.ReadFromJsonAsync<SourceResponse>())!.Id;

        var journal = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/sources",
            new CreateSourceRequest("Voss's diary", "JournalEntry", "PartyVisible", Body: "Dear diary."));
        var journalId = (await journal.Content.ReadFromJsonAsync<SourceResponse>())!.Id;

        var list = await _anonymous.GetFromJsonAsync<List<SourceListItemResponse>>(
            "/api/public/worlds/black-harbor/sources");
        var journalDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{journalId}");

        Assert.That(list!.Select(s => s.Id), Is.SupersetOf(new[] { sessionId, importedId }));
        Assert.That(list!.Select(s => s.Id), Does.Not.Contain(journalId),
            "only SessionNote and ImportedNote appear in the public list");
        Assert.That(journalDetail.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "party-visible sources of other types stay reachable by direct link (timeline points)");
    }

    [Test]
    public async Task PublicSources_PartyVisibleReadable_GmOnly404()
    {
        var scenario = await SetupPublicWorldAsync();

        var partySource = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/sources",
            new CreateSourceRequest("Session 1", "SessionNote", "PartyVisible", Body: "The **harbor** burned."));
        var partyId = (await partySource.Content.ReadFromJsonAsync<SourceResponse>())!.Id;

        var gmSource = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/sources",
            new CreateSourceRequest("GM prep", "GMNote", "GMOnly", Body: "The villain is the mayor."));
        var gmId = (await gmSource.Content.ReadFromJsonAsync<SourceResponse>())!.Id;

        var list = await _anonymous.GetFromJsonAsync<List<SourceListItemResponse>>(
            "/api/public/worlds/black-harbor/sources");
        var partyDetail = await _anonymous.GetFromJsonAsync<SourceResponse>(
            $"/api/public/worlds/black-harbor/sources/{partyId}");
        var gmDetail = await _anonymous.GetAsync($"/api/public/worlds/black-harbor/sources/{gmId}");

        Assert.That(list!.Select(s => s.Id), Does.Contain(partyId));
        Assert.That(list!.Select(s => s.Id), Does.Not.Contain(gmId));
        Assert.That(partyDetail!.Body, Does.Contain("harbor"));
        Assert.That(gmDetail.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PublicTimeline_Returns200()
    {
        await SetupPublicWorldAsync();

        var response = await _anonymous.GetAsync("/api/public/worlds/black-harbor/timeline");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AnonymousClient_AuthenticatedEndpoints_Still401()
    {
        var scenario = await SetupPublicWorldAsync();

        var worlds = await _anonymous.GetAsync("/api/worlds");
        var ask = await _anonymous.PostAsJsonAsync($"/api/worlds/{scenario.World.Id}/ask", new { question = "hi" });
        var library = await _anonymous.GetAsync($"/api/worlds/{scenario.World.Id}/library");

        Assert.That(worlds.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(ask.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(library.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task PublicNamespace_HasNoLibraryOrAskRoutes()
    {
        await SetupPublicWorldAsync();

        var library = await _anonymous.GetAsync("/api/public/worlds/black-harbor/library");
        var ask = await _anonymous.PostAsJsonAsync("/api/public/worlds/black-harbor/ask", new { question = "hi" });

        // No such endpoints exist; unmatched routes hit the fallback policy first (401)
        // rather than 404 — either way, nothing anonymous is served.
        Assert.That(library.StatusCode, Is.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized));
        Assert.That(ask.StatusCode, Is.AnyOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task SlugTakenByAnotherWorld_Returns409()
    {
        var scenario = await SetupPublicWorldAsync(slug: "black-harbor");

        // A second world owned by the same GM tries to claim the same slug.
        var created = await scenario.GmClient.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Second World", null, null));
        var secondId = (await created.Content.ReadFromJsonAsync<WorldResponse>())!.Id;

        var response = await scenario.GmClient.PutAsJsonAsync($"/api/worlds/{secondId}",
            new UpdateWorldRequest(PublicSlug: "black-harbor"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }
}
