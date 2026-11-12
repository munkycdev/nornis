using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Extensions;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Prep;

/// <summary>
/// Prep's routes (feature 29, Requirement 1). Every one is GM-only, so every one is asked as a
/// Player, an Observer and a GM viewing as a Player, and every one must answer 403 before it
/// touches anything. The GM tests walk the routes end to end.
/// </summary>
[TestFixture]
public class PrepEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private static string Base(Guid worldId) => $"/api/worlds/{worldId}/prep";

    private static async Task<AdventureResponse> CreateAsync(HttpClient gm, Guid worldId, string title = "The Silver Key")
    {
        var response = await gm.PostAsJsonAsync(Base(worldId), new CreateAdventureRequest(title, "A heist.", null));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<AdventureResponse>())!;
    }

    /// <summary>Every Prep route, as a request a non-GM might send.</summary>
    private static IEnumerable<(string Name, Func<HttpClient, Guid, Guid, Guid, Task<HttpResponseMessage>> Send)> Routes()
    {
        yield return ("list", (c, w, _, _) => c.GetAsync(Base(w)));
        yield return ("create", (c, w, _, _) => c.PostAsJsonAsync(Base(w), new CreateAdventureRequest("Mine now", null, null)));
        yield return ("import", (c, w, _, _) => c.PostAsJsonAsync($"{Base(w)}/import",
            new ImportAdventureRequest([new ImportAdventureFile("a.md", "# A\n\nText.")], null)));
        yield return ("get", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}"));
        yield return ("update", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}", new UpdateAdventureRequest("Renamed", null, null)));
        yield return ("details", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/details",
            new UpdateAdventureDetailsRequest("Mine", null, null, null)));
        yield return ("typography", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/typography",
            new UpdateBookTypographyRequest("Lora", null, null)));
        yield return ("ai declared", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/ai-declared", new SetAiDeclaredRequest(true)));
        yield return ("delete", (c, w, a, _) => c.DeleteAsync($"{Base(w)}/{a}"));
        yield return ("save section", (c, w, a, s) => c.PutAsJsonAsync($"{Base(w)}/{a}/sections/{s}",
            new SaveAdventureSectionRequest("Notes", "Mine", "Body", DateTimeOffset.UtcNow)));
        yield return ("add section", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/sections",
            new AddAdventureSectionRequest("Notes", "Mine", null)));
        yield return ("delete section", (c, w, a, s) => c.DeleteAsync($"{Base(w)}/{a}/sections/{s}"));
        yield return ("reorder", (c, w, a, s) => c.PutAsJsonAsync($"{Base(w)}/{a}/order", new ReorderAdventureSectionsRequest([s])));
        yield return ("undo", (c, w, a, s) => c.PostAsync($"{Base(w)}/{a}/sections/{s}/undo", null));
        yield return ("budget", (c, w, _, _) => c.GetAsync($"/api/worlds/{w}/prep-budget"));
        yield return ("booklet", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/booklet"));
        yield return ("file booklet", (c, w, a, _) => c.PostAsync($"{Base(w)}/{a}/booklet/file", null));
        yield return ("export", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/export", new ExportAdventureRequest(null)));
        yield return ("conversation", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/conversation"));
        yield return ("send message", (c, w, a, s) => c.PostAsJsonAsync($"{Base(w)}/{a}/conversation/messages",
            new SendConversationMessageRequest("Who else could the cult have recruited?", s)));
        yield return ("clear conversation", (c, w, a, _) => c.DeleteAsync($"{Base(w)}/{a}/conversation"));
        yield return ("put in section", (c, w, a, s) => c.PostAsJsonAsync($"{Base(w)}/{a}/sections/{s}/put",
            new PutInSectionRequest(Guid.NewGuid(), "Replace", DateTimeOffset.UtcNow)));
        yield return ("save draft", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/draft",
            new SaveAdventureDraftRequest("# Mine", null)));
        yield return ("undo draft", (c, w, a, _) => c.PostAsync($"{Base(w)}/{a}/draft/undo", null));
        yield return ("apply edit", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/draft/edits",
            new ApplyDraftEditRequest(Guid.NewGuid(), 0, null)));
        yield return ("plan split", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/draft/plan", new PlanDraftRequest(false)));
        yield return ("split", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/draft/split",
            new SplitDraftRequest([new DraftSplitPointRequest(1, "A", "Scene")], "Replace", null)));
        yield return ("gather", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/draft/gather", new GatherDraftRequest(null)));
        yield return ("put in draft", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/draft/put",
            new PutInDraftRequest(Guid.NewGuid(), "Replace", null)));
        yield return ("brief", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/brief", new SetTableBriefRequest("Mine")));
        yield return ("source books", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/source-books", new SetSourceBooksRequest([])));
        yield return ("handouts", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/handouts", new SetHandoutsRequest([])));
        yield return ("props", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/props"));
        yield return ("cover", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/cover"));
        yield return ("write cover brief", (c, w, a, _) => c.PostAsync($"{Base(w)}/{a}/cover/brief", null));
        yield return ("save cover brief", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/cover/brief", new CoverBriefRequest("Mine")));
        yield return ("generate cover", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/cover/generate", new CoverBriefRequest("Mine")));
        yield return ("choose cover", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/cover/choose", new ChooseCoverRequest(Guid.NewGuid())));
        yield return ("upload cover", (c, w, a, _) => c.PutAsync($"{Base(w)}/{a}/cover/image", new ByteArrayContent(CoverPng(1200, 1800))));
        yield return ("remove cover", (c, w, a, _) => c.DeleteAsync($"{Base(w)}/{a}/cover"));
        yield return ("cover layout", (c, w, a, _) => c.PutAsJsonAsync($"{Base(w)}/{a}/cover/layout", new SetCoverLayoutRequest("Framed")));
        yield return ("cover image", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/cover/image"));
        yield return ("cover option", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/cover/options/{Guid.NewGuid()}"));
    }

    /// <summary>A PNG header with the given size: the API reads only that.</summary>
    private static byte[] CoverPng(int width, int height)
    {
        var bytes = new byte[40];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    [Test]
    public async Task Gm_UploadsCoverArt_SeesItAndItsLayout_AndRemovesIt_WithNoAiUseRecorded()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var cover = $"{Base(worldId)}/{adventure.Id}/cover";

        var empty = (await scenario.GmClient.GetFromJsonAsync<CoverStateResponse>(cover))!;
        var upload = await scenario.GmClient.PutAsync($"{cover}/image", new ByteArrayContent(CoverPng(1200, 1800)));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), await upload.Content.ReadAsStringAsync());
        var image = await scenario.GmClient.GetAsync($"{cover}/image");
        var framed = await scenario.GmClient.PutAsJsonAsync($"{cover}/layout", new SetCoverLayoutRequest("Framed"));
        var framedState = (await framed.Content.ReadFromJsonAsync<CoverStateResponse>())!;
        var badLayout = await scenario.GmClient.PutAsJsonAsync($"{cover}/layout", new SetCoverLayoutRequest("Sideways"));
        var removed = (await (await scenario.GmClient.DeleteAsync(cover)).Content.ReadFromJsonAsync<CoverStateResponse>())!;
        var reloaded = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;

        Assert.Multiple(async () =>
        {
            Assert.That(empty.Cover, Is.Null);
            Assert.That(empty.CanGenerate, Is.False, "no OpenAI key in the test world");
            Assert.That(empty.GenerateBlocker, Does.Contain("OpenAI key"));
            Assert.That(empty.ImagesCap, Is.EqualTo(12));
            Assert.That(image.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(image.Content.Headers.ContentType!.MediaType, Is.EqualTo("image/png"));
            Assert.That(await image.Content.ReadAsByteArrayAsync(), Is.EqualTo(CoverPng(1200, 1800)));
            Assert.That(framedState.Cover!.Layout, Is.EqualTo("Framed"));
            Assert.That(framedState.Cover.Source, Is.EqualTo("Uploaded"));
            Assert.That(badLayout.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(removed.Cover, Is.Null);
            Assert.That(reloaded.AiUse!.Locked, Is.False);
        });
    }

    [Test]
    public async Task Props_AreAPdfOfThePropBoxes_Or404WhenThereAreNone()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var none = await scenario.GmClient.GetAsync($"{Base(worldId)}/{adventure.Id}/props");
        await WriteOverviewAsync(scenario.GmClient, worldId, adventure, "The mayor's seal.\n\n> [!PROP]\n> The forged letter.");
        var props = await scenario.GmClient.GetAsync($"{Base(worldId)}/{adventure.Id}/props");

        Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await none.Content.ReadAsStringAsync(), Does.Contain("no_props"));
        Assert.That(props.StatusCode, Is.EqualTo(HttpStatusCode.OK), await props.Content.ReadAsStringAsync());
        Assert.That(props.Content.Headers.ContentType!.MediaType, Is.EqualTo("application/pdf"));
    }

    [Test]
    public async Task Handouts_RefuseADocumentThatIsntAMapOrHandout()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var missing = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/handouts", new SetHandoutsRequest([Guid.NewGuid()]));
        var cleared = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/handouts", new SetHandoutsRequest([]));

        Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(cleared.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await cleared.Content.ReadFromJsonAsync<AdventureResponse>())!.Handouts, Is.Empty);
    }

    [Test]
    public async Task GeneratingACover_WithNoOpenAiKey_IsRefusedWithTheKeyCallToAction_AndCostsNothing()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var cover = $"{Base(worldId)}/{adventure.Id}/cover";

        var response = await scenario.GmClient.PostAsJsonAsync($"{cover}/generate", new CoverBriefRequest("A bathhouse at dusk."));
        var after = (await scenario.GmClient.GetFromJsonAsync<CoverStateResponse>(cover))!;

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await response.Content.ReadAsStringAsync());
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("ai_key_required"));
        Assert.That(after.ImagesGenerated, Is.Zero);
        Assert.That(after.Options, Is.Empty);
    }

    [Test]
    [Category("Authorization")]
    public async Task EveryRoute_RefusesPlayersObserversAndAGmViewingAsAPlayer()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var sectionId = adventure.Sections[0].Id;

        var viewingAsPlayer = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-kelda-source", email: "kelda@blackharbor.com", nickname: "Kelda");
        viewingAsPlayer.DefaultRequestHeaders.Add(HttpContextExtensions.ViewAsHeaderName, "Player");

        var callers = new[] { ("player", scenario.PlayerClient), ("observer", scenario.ObserverClient), ("gm as player", viewingAsPlayer) };
        var failures = new List<string>();
        foreach (var (caller, client) in callers)
        {
            foreach (var (route, send) in Routes())
            {
                var response = await send(client, worldId, adventure.Id, sectionId);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                {
                    failures.Add($"{caller} {route}: {(int)response.StatusCode}");
                }
            }
        }

        Assert.That(failures, Is.Empty);

        // And nothing they sent landed: the GM still sees one untouched Adventure.
        var after = await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}");
        Assert.That(after!.Title, Is.EqualTo("The Silver Key"));
        Assert.That(after.Details, Is.EqualTo(new AdventureDetailsResponse(null, null, null, null)));
        Assert.That(after.Sections.Select(s => s.Id), Is.EqualTo(adventure.Sections.Select(s => s.Id)));
        Assert.That((after.Draft, after.TableBrief), Is.EqualTo(((AdventureDraftResponse?)null, (string?)null)));
        var list = await scenario.GmClient.GetFromJsonAsync<List<AdventureSummaryResponse>>(Base(worldId));
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That(list![0].IsExported, Is.False);
        var conversation = await scenario.GmClient.GetFromJsonAsync<ConversationResponse>($"{Base(worldId)}/{adventure.Id}/conversation");
        Assert.That(conversation!.Messages, Is.Empty);
        Assert.That(_factory.FakeDrafting.Requests, Is.Empty);
    }

    [Test]
    public async Task TheViewAsCaller_IsTheScenarioGm()
    {
        // The test above leans on "auth0|gm-kelda-source" being the scenario's GM. If that ever stops
        // being true, its view-as caller would be a stranger and pass for the wrong reason.
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var gm = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-kelda-source", email: "kelda@blackharbor.com", nickname: "Kelda");

        var response = await gm.GetAsync(Base(scenario.World.Id));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Gm_CreatesAnAdventure_WithTheSkeleton_AndFindsItBySlug()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var adventure = await CreateAsync(scenario.GmClient, scenario.World.Id);

        Assert.That(adventure.Sections.Select(s => s.Kind),
            Is.EqualTo(["Overview", "SessionClock", "Chapter", "Scene", "Scene", "Scene", "QuickReference"]));
        Assert.That(adventure.Slug, Is.Not.Null.And.Not.Empty);
        Assert.That(adventure.Sections.Select(s => s.IsFilled), Is.All.False, "an untouched skeleton, scene templates included, is unwritten");

        var bySlug = await scenario.GmClient.GetAsync($"{Base(scenario.World.Id)}/{adventure.Slug}");
        Assert.That(bySlug.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await bySlug.Content.ReadFromJsonAsync<AdventureResponse>())!.Id, Is.EqualTo(adventure.Id));

        var list = await scenario.GmClient.GetFromJsonAsync<List<AdventureSummaryResponse>>(Base(scenario.World.Id));
        Assert.That(list!.Single().SectionCount, Is.EqualTo(7));
        Assert.That(list!.Single().FilledSectionCount, Is.Zero);
    }

    [Test]
    public async Task Import_RoutesToImport_NotToGetByKey()
    {
        // "import" is a literal segment beside {key}; a POST must never be read as an Adventure named "import".
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(scenario.World.Id)}/import",
            new ImportAdventureRequest([new ImportAdventureFile("key.md", "# The Silver Key\n\n# Scene 1: The Docks\n\n> Fog.")], null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var adventure = (await response.Content.ReadFromJsonAsync<AdventureResponse>())!;
        Assert.That(adventure.Title, Is.EqualTo("The Silver Key"));
        Assert.That(adventure.Sections.Select(s => s.Kind), Does.Contain("Scene"));
    }

    [Test]
    public async Task Import_ReadsTheFrontMatter_AndSaysWhichKeysItLeftOut()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(scenario.World.Id)}/import",
            new ImportAdventureRequest([new ImportAdventureFile("spa.md",
                "---\nsystem: Shadowdark\nlevels: 1-3\ntype: Adventure\n---\n# The Slime Spa\n\nWelcome.")], null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var adventure = (await response.Content.ReadFromJsonAsync<AdventureResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(adventure.Details, Is.EqualTo(new AdventureDetailsResponse("Shadowdark", null, "1-3", null)));
            Assert.That(adventure.IgnoredDetails, Is.EqualTo((string[])["type"]));
        });
    }

    [Test]
    public async Task Gm_SavesASection_AndAStaleSaveIsRefused()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var adventure = await CreateAsync(scenario.GmClient, scenario.World.Id);
        var overview = adventure.Sections[0];
        var url = $"{Base(scenario.World.Id)}/{adventure.Id}/sections/{overview.Id}";

        var saved = await scenario.GmClient.PutAsJsonAsync(url,
            new SaveAdventureSectionRequest("Overview", "Overview", "The key opens the vault.", overview.UpdatedAt));
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        var section = (await saved.Content.ReadFromJsonAsync<SavedAdventureSectionResponse>())!;
        Assert.That(section.BodyMarkdown, Is.EqualTo("The key opens the vault."));
        Assert.That(section.IsFilled, Is.True);

        var stale = await scenario.GmClient.PutAsJsonAsync(url,
            new SaveAdventureSectionRequest("Overview", "Overview", "An older tab's text.", overview.UpdatedAt));
        Assert.That(stale.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var after = await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(scenario.World.Id)}/{adventure.Id}");
        Assert.That(after!.Sections[0].BodyMarkdown, Is.EqualTo("The key opens the vault."));
    }

    [Test]
    public async Task ASection_CarriesItsBlockWarnings_OnSaveAndOnLoad()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var adventure = await CreateAsync(scenario.GmClient, scenario.World.Id);
        var overview = adventure.Sections[0];
        var url = $"{Base(scenario.World.Id)}/{adventure.Id}/sections/{overview.Id}";
        const string warning = "The first stat block has no name, so Add to the record can't file it on anyone. Start it with \"# Name\".";

        var saved = await scenario.GmClient.PutAsJsonAsync(url,
            new SaveAdventureSectionRequest("Overview", "Overview", "A guard.\n\n```statblock\nAC: 12\n```", overview.UpdatedAt));
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        var section = (await saved.Content.ReadFromJsonAsync<SavedAdventureSectionResponse>())!;
        var after = await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(scenario.World.Id)}/{adventure.Id}");

        Assert.Multiple(() =>
        {
            Assert.That(section.BlockWarnings, Is.EqualTo((string[])[warning]));
            Assert.That(after!.Sections[0].BlockWarnings, Is.EqualTo((string[])[warning]));
            Assert.That(after.Sections[1].BlockWarnings, Is.Empty, "a section with no blocks has none");
        });
    }

    [Test]
    public async Task SaveSection_WithAnUnknownKind_Is400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var adventure = await CreateAsync(scenario.GmClient, scenario.World.Id);
        var overview = adventure.Sections[0];

        var response = await scenario.GmClient.PutAsJsonAsync(
            $"{Base(scenario.World.Id)}/{adventure.Id}/sections/{overview.Id}",
            new SaveAdventureSectionRequest("Epilogue", "Overview", "x", overview.UpdatedAt));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Gm_AddsReordersAndDeletesSections()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var added = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/sections",
            new AddAdventureSectionRequest("Notes", "Loose ends", adventure.Sections[0].Id));
        Assert.That(added.StatusCode, Is.EqualTo(HttpStatusCode.OK), await added.Content.ReadAsStringAsync());
        var withNotes = (await added.Content.ReadFromJsonAsync<AdventureResponse>())!;
        Assert.That(withNotes.Sections[1].Title, Is.EqualTo("Loose ends"));

        var reversed = withNotes.Sections.Select(s => s.Id).Reverse().ToList();
        var reordered = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/order",
            new ReorderAdventureSectionsRequest(reversed));
        Assert.That(reordered.StatusCode, Is.EqualTo(HttpStatusCode.OK), await reordered.Content.ReadAsStringAsync());
        Assert.That((await reordered.Content.ReadFromJsonAsync<AdventureResponse>())!.Sections.Select(s => s.Id), Is.EqualTo(reversed));

        var deleted = await scenario.GmClient.DeleteAsync($"{Base(worldId)}/{adventure.Id}/sections/{reversed[0]}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await deleted.Content.ReadFromJsonAsync<AdventureResponse>())!.Sections, Has.Count.EqualTo(7));
    }

    [Test]
    public async Task Gm_UpdatesAndDeletesAnAdventure()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var updated = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}",
            new UpdateAdventureRequest("The Golden Key", null, null));
        Assert.That(updated.StatusCode, Is.EqualTo(HttpStatusCode.OK), await updated.Content.ReadAsStringAsync());
        var after = (await updated.Content.ReadFromJsonAsync<AdventureResponse>())!;
        Assert.That(after.Title, Is.EqualTo("The Golden Key"));
        Assert.That(after.Pitch, Is.Null);

        var deleted = await scenario.GmClient.DeleteAsync($"{Base(worldId)}/{adventure.Id}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var gone = await scenario.GmClient.GetAsync($"{Base(worldId)}/{adventure.Id}");
        Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Undo_WithNoRevision_Is409()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var adventure = await CreateAsync(scenario.GmClient, scenario.World.Id);

        var response = await scenario.GmClient.PostAsync(
            $"{Base(scenario.World.Id)}/{adventure.Id}/sections/{adventure.Sections[0].Id}/undo", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    // ---------------------------------------------------------- streams and budget --

    private static async Task<List<SseItem<string>>> ReadEventsAsync(HttpResponseMessage response)
    {
        var items = new List<SseItem<string>>();
        await foreach (var item in SseParser.Create(await response.Content.ReadAsStreamAsync()).EnumerateAsync())
        {
            items.Add(item);
        }

        return items;
    }

    [Test]
    public async Task Budget_ReadsThisMonthsPrepSpend_AgainstTheDefaultCeiling()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var budget = await scenario.GmClient.GetFromJsonAsync<PrepBudgetResponse>($"/api/worlds/{scenario.World.Id}/prep-budget");

        Assert.That(budget!.MonthlyBudgetUsd, Is.EqualTo(10.00m));
        Assert.That(budget.SpentThisMonthUsd, Is.Zero);
        Assert.That(budget.IsExceeded, Is.False);
    }

    [Test]
    public async Task AnAdventureCalledBudget_IsStillReachableBySlug()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var created = await CreateAsync(scenario.GmClient, scenario.World.Id, "Budget");

        var bySlug = await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(scenario.World.Id)}/{created.Slug}");

        Assert.That(created.Slug, Is.EqualTo("budget"));
        Assert.That(bySlug!.Id, Is.EqualTo(created.Id));
    }

    private static async Task<AdventureResponse> WriteOverviewAsync(HttpClient gm, Guid worldId, AdventureResponse adventure, string body)
    {
        var overview = adventure.Sections[0];
        var saved = await gm.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/sections/{overview.Id}",
            new SaveAdventureSectionRequest("Overview", "Overview", body, overview.UpdatedAt));
        Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
        return (await gm.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;
    }

    [Test]
    public async Task Booklet_IsThePdf_ShownInline_AndNeverCached()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await WriteOverviewAsync(scenario.GmClient, worldId,
            await CreateAsync(scenario.GmClient, worldId), "> The vault door is cold.");

        var response = await scenario.GmClient.GetAsync($"{Base(worldId)}/{adventure.Id}/booklet");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/pdf"));
            Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(response.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("inline"));
            Assert.That(response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName,
                Is.EqualTo("The Silver Key.pdf"));
            Assert.That(response.Headers.CacheControl?.NoStore, Is.True);
            Assert.That(response.Headers.CacheControl?.Private, Is.True);
        });
    }

    [Test]
    public async Task FileBooklet_StoresThePdf_AsTheAdventuresGmOnlyPrint()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PostAsync($"{Base(worldId)}/{adventure.Id}/booklet/file", null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        var filed = (await response.Content.ReadFromJsonAsync<LibraryDocumentResponse>())!;
        var reloaded = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;
        var blob = _factory.BlobStorage.Blobs.Values.Select(b => b.Content).Single(c => c.Length == filed.SizeBytes);

        Assert.Multiple(() =>
        {
            Assert.That(filed.Title, Does.StartWith("The Silver Key, printed "));
            Assert.That(filed.Kind, Is.EqualTo("Handout"));
            Assert.That(filed.Visibility, Is.EqualTo("GMOnly"));
            Assert.That(filed.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(filed.FileName, Is.EqualTo("The Silver Key.pdf"));
            Assert.That(filed.AdventureId, Is.EqualTo(adventure.Id));
            Assert.That(filed.Status, Is.Not.EqualTo("PendingUpload"));
            Assert.That(Encoding.ASCII.GetString(blob, 0, 5), Is.EqualTo("%PDF-"));
            Assert.That(reloaded.LastPrintedDocumentId, Is.EqualTo(filed.Id));
        });
    }

    [Test]
    public async Task AnUpload_NamingAnAdventure_IsRefused()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PostAsJsonAsync($"/api/worlds/{worldId}/library/request-upload",
            new RequestLibraryUploadRequest("Mine", "mine.pdf", "application/pdf", 1024, "Handout", "GMOnly", adventure.Id));

        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(body, Does.Contain("prints_are_filed_from_prep"));
        });
    }

    [Test]
    public async Task Gm_SavesTheDetails_AndGetsTheResolvedPreview()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/details",
            new UpdateAdventureDetailsRequest("Shadowdark", " 1st ", "1–3", 2026));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<AdventureResponse>())!;
        var reloaded = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;

        Assert.Multiple(() =>
        {
            Assert.That(saved.Details, Is.EqualTo(new AdventureDetailsResponse("Shadowdark", "1st", "1–3", 2026)));
            Assert.That(reloaded.Details, Is.EqualTo(saved.Details));
            Assert.That(reloaded.PrintDetails.Title, Is.EqualTo("The Silver Key"));
            Assert.That(reloaded.PrintDetails.GameSystem, Is.EqualTo("Shadowdark"));
            Assert.That(reloaded.PrintDetails.Type, Is.EqualTo("Nornis adventure"));
            Assert.That(reloaded.PrintDetails.Publisher, Is.EqualTo("Nornis Press"));
            Assert.That(reloaded.PrintDetails.Author, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Gm_SetsTheBooksType_AndTheAdventureCarriesIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        Assert.That(adventure.Typography, Is.EqualTo(new BookTypographyResponse(null, null, null)), "nothing chosen yet");

        var response = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/typography",
            new UpdateBookTypographyRequest("EbGaramond", 11.5m, 1.4m));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var reloaded = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;

        Assert.That(reloaded.Typography, Is.EqualTo(new BookTypographyResponse("EbGaramond", 11.5m, 1.4m)));
    }

    [TestCase("Comic Sans", null, null)]
    [TestCase(null, 20.0, null)]
    [TestCase(null, null, 1.33)]
    public async Task Gm_SetsATypeOutsideTheBounds_Is400(string? font, double? size, double? spacing)
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/typography",
            new UpdateBookTypographyRequest(font, (decimal?)size, (decimal?)spacing));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("invalid_typography"));
    }

    [Test]
    public async Task Gm_TicksAiUse_AndTheAdventureAndItsPreviewSaySo()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/ai-declared", new SetAiDeclaredRequest(true));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var reloaded = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;

        Assert.Multiple(() =>
        {
            Assert.That(adventure.AiUse, Is.EqualTo(new AdventureAiUseResponse(false, false, false, false, "None")));
            Assert.That(reloaded.AiUse, Is.EqualTo(new AdventureAiUseResponse(false, false, true, false, "Declared")));
            Assert.That(reloaded.PrintDetails.AiUse, Is.EqualTo("Declared"));
        });
    }

    [Test]
    public async Task Details_OutOfBounds_Is400_InvalidDetails_AndNothingSaved()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/details",
            new UpdateAdventureDetailsRequest("Shadowdark", new string('x', 51), null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("invalid_details"));
        var after = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!;
        Assert.That(after.Details, Is.EqualTo(new AdventureDetailsResponse(null, null, null, null)));
    }

    [Test]
    public async Task Gm_ExportsTheAdventure_AsAGmOnlySource_QueuedForExtraction()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await WriteOverviewAsync(scenario.GmClient, worldId,
            await CreateAsync(scenario.GmClient, worldId), "The key opens the vault.");

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/export", new ExportAdventureRequest(null));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var export = (await response.Content.ReadFromJsonAsync<AdventureExportResponse>())!;

        var source = await scenario.GmClient.GetFromJsonAsync<SourceResponse>($"/api/worlds/{worldId}/sources/{export.SourceId}");
        Assert.Multiple(() =>
        {
            Assert.That(export.Title, Is.EqualTo("The Silver Key (prep)"));
            Assert.That(export.Slug, Is.Not.Null.And.Not.Empty);
            Assert.That(source!.Type, Is.EqualTo("AdventureExport"));
            Assert.That(source.Visibility, Is.EqualTo("GMOnly"));
            Assert.That(source.AdventureId, Is.EqualTo(adventure.Id));
            Assert.That(source.Body, Does.Contain("The key opens the vault."));
            Assert.That(_factory.ExtractionQueueClient.SentMessages.Count(m => m.SourceId == export.SourceId), Is.EqualTo(1));
        });

        // The Prep list now counts it as exported.
        var list = await scenario.GmClient.GetFromJsonAsync<List<AdventureSummaryResponse>>(Base(worldId));
        Assert.That(list!.Single(a => a.Id == adventure.Id).IsExported, Is.True);

        // GM-only all the way down: the Player can't open what the export filed.
        var asPlayer = await scenario.PlayerClient.GetAsync($"/api/worlds/{worldId}/sources/{export.SourceId}");
        Assert.That(asPlayer.StatusCode, Is.EqualTo(HttpStatusCode.NotFound).Or.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Export_OfAnUntouchedSkeleton_Is400_AndFilesNothing()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var adventure = await CreateAsync(scenario.GmClient, scenario.World.Id);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"{Base(scenario.World.Id)}/{adventure.Id}/export", new ExportAdventureRequest(null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code, Is.EqualTo("nothing_to_export"));
        Assert.That(_factory.ExtractionQueueClient.SentMessages, Is.Empty);
    }

    // ------------------------------------------------------- side conversation --

    [Test]
    public async Task ATurn_StreamsTheAnswer_SavesBoth_AndPutInSectionThenUndoGivesTheTextBack()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var scene = adventure.Sections.First(s => s.Kind == "Scene");
        _factory.FakeDrafting.Text = "> Fog rolls in off the water.";

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("Open the scene for me.", scene.Id));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        var events = await ReadEventsAsync(response);
        var deltas = events.Where(e => e.EventType == DraftStreamEventNames.Delta)
            .Select(e => JsonSerializer.Deserialize<DraftStreamDeltaResponse>(e.Data, JsonSerializerOptions.Web)!.Text);
        var answer = JsonSerializer.Deserialize<ConversationMessageResponse>(events[^1].Data, JsonSerializerOptions.Web)!;
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(deltas), Is.EqualTo("> Fog rolls in off the water."));
            Assert.That(events[^1].EventType, Is.EqualTo(DraftStreamEventNames.Done));
            Assert.That(answer.Role, Is.EqualTo("Assistant"));
            Assert.That(answer.Content, Is.EqualTo("> Fog rolls in off the water."));
            Assert.That(_factory.FakeDrafting.Requests.Single().UserMessage, Does.Contain("Open the scene for me."));
        });

        var conversation = await scenario.GmClient.GetFromJsonAsync<ConversationResponse>($"{Base(worldId)}/{adventure.Id}/conversation");
        Assert.That(conversation!.Messages.Select(m => m.Role), Is.EqualTo(["User", "Assistant"]));
        Assert.That(conversation.IsFull, Is.False);

        var put = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/sections/{scene.Id}/put",
            new PutInSectionRequest(answer.Id, "Replace", scene.UpdatedAt));
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.OK), await put.Content.ReadAsStringAsync());
        var section = (await put.Content.ReadFromJsonAsync<AdventureSectionResponse>())!;
        Assert.That(section.BodyMarkdown, Is.EqualTo("> Fog rolls in off the water."));
        Assert.That(section.RevisionCount, Is.EqualTo(1));
        Assert.That(_factory.FakeDrafting.Requests, Has.Count.EqualTo(1), "Put in section makes no AI call");

        var undone = await scenario.GmClient.PostAsync($"{Base(worldId)}/{adventure.Id}/sections/{scene.Id}/undo", null);
        var restored = (await undone.Content.ReadFromJsonAsync<AdventureResponse>())!.Sections.Single(s => s.Id == scene.Id);
        Assert.That(restored.BodyMarkdown, Is.EqualTo(scene.BodyMarkdown));
    }

    [Test]
    public async Task WebResearch_OnNornissOwnKey_IsOffWithItsReason_AndATurnAskingForItIsA409()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var conversation = await scenario.GmClient.GetFromJsonAsync<ConversationResponse>($"{Base(worldId)}/{adventure.Id}/conversation");
        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("What are Waterdeep's wards?", null, WebResearch: true));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();

        Assert.Multiple(() =>
        {
            Assert.That(conversation!.WebResearch.Available, Is.False, "the test world has no key of its creator's");
            Assert.That(conversation.WebResearch.Reason, Is.EqualTo("Web research runs on your own AI key. Add one under Your account, AI."));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(error!.Code, Is.EqualTo("web_research_unavailable"));
            Assert.That(_factory.FakeDrafting.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task ABlankMessage_IsAPlain400_NotAStream()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("  ", null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(_factory.FakeDrafting.Requests, Is.Empty);
    }

    [Test]
    public async Task Clear_Is204_AndLeavesTheConversationEmpty()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var sent = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("Who else?", null));
        await ReadEventsAsync(sent);

        var cleared = await scenario.GmClient.DeleteAsync($"{Base(worldId)}/{adventure.Id}/conversation");
        var conversation = await scenario.GmClient.GetFromJsonAsync<ConversationResponse>($"{Base(worldId)}/{adventure.Id}/conversation");

        Assert.That(cleared.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(conversation!.Messages, Is.Empty);
    }

    [Test]
    public async Task PutInSection_WithAnUnknownMode_Is400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var scene = adventure.Sections.First(s => s.Kind == "Scene");

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/sections/{scene.Id}/put",
            new PutInSectionRequest(Guid.NewGuid(), "Overwrite", scene.UpdatedAt));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code, Is.EqualTo("invalid_mode"));
    }

    [Test]
    public async Task Gm_StartsADraft_SavesItOnItsStamp_AndAStaleSaveIs409()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        Assert.That(adventure.Draft, Is.Null, "a new Adventure has no draft");

        var started = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft",
            new SaveAdventureDraftRequest("# The Slurping Spa", null));
        var draft = (await started.Content.ReadFromJsonAsync<AdventureDraftResponse>())!;
        var next = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft",
            new SaveAdventureDraftRequest("# The Slurping Spa\n\nSlimes.", draft.UpdatedAt));
        var stale = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft",
            new SaveAdventureDraftRequest("lost", draft.UpdatedAt));
        var undo = await scenario.GmClient.PostAsync($"{Base(worldId)}/{adventure.Id}/draft/undo", null);

        var after = await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}");
        Assert.Multiple(() =>
        {
            Assert.That((started.StatusCode, next.StatusCode, stale.StatusCode, undo.StatusCode),
                Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.Conflict)),
                "the GM's own typing keeps no revision, so there is nothing to undo");
            Assert.That(after!.Draft!.Markdown, Is.EqualTo("# The Slurping Spa\n\nSlimes."));
            Assert.That(after.Sections.Select(s => s.Id), Is.EqualTo(adventure.Sections.Select(s => s.Id)), "the draft is not a section");
        });
    }

    [Test]
    public async Task InDesignMode_AnAnswersEdits_GoIntoTheDraft_AsOneUndo_AndNeverStreamIntoTheChat()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var started = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft",
            new SaveAdventureDraftRequest("# The Spa\n\nThe spa is quiet.", null));
        var draft = (await started.Content.ReadFromJsonAsync<AdventureDraftResponse>())!;
        _factory.FakeDrafting.Text = "Louder.\n\n````edit\n<<<\nThe spa is quiet.\n===\nThe spa hums with slime.\n>>>\n````";

        var sent = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("Make it louder.", null, "Draft"));
        var events = await ReadEventsAsync(sent);
        var answer = JsonSerializer.Deserialize<ConversationMessageResponse>(events[^1].Data, JsonSerializerOptions.Web)!;
        var streamed = string.Concat(events.Where(e => e.EventType == DraftStreamEventNames.Delta)
            .Select(e => JsonSerializer.Deserialize<DraftStreamDeltaResponse>(e.Data, JsonSerializerOptions.Web)!.Text));
        var after = (await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}"))!.Draft!;

        Assert.Multiple(() =>
        {
            Assert.That(_factory.FakeDrafting.Requests.Single().UserMessage, Does.Contain("The spa is quiet."), "the turn saw the draft");
            Assert.That(streamed, Does.Not.Contain("hums"), "the edit's text goes into the draft, not the chat");
            Assert.That(events.Select(e => e.EventType), Does.Contain(DraftStreamEventNames.Writing));
            Assert.That(answer.Edits, Is.EqualTo([new DraftEditResponse("The spa is quiet.", "The spa hums with slime.")]));
            Assert.That(answer.Prose, Is.EqualTo("Louder."));
            Assert.That(answer.DraftEditOutcomes, Is.EqualTo(["Applied"]));
            Assert.That(after.Markdown, Is.EqualTo("# The Spa\n\nThe spa hums with slime."));
            Assert.That(after.RevisionCount, Is.EqualTo(1));
        });

        var again = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft/edits",
            new ApplyDraftEditRequest(answer.Id, 0, after.UpdatedAt));
        Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "the passage is gone, so the edit no longer fits");
        Assert.That((await again.Content.ReadFromJsonAsync<ErrorResponse>())!.Code, Is.EqualTo("edit_no_longer_fits"));

        var undone = await scenario.GmClient.PostAsync($"{Base(worldId)}/{adventure.Id}/draft/undo", null);
        Assert.That((await undone.Content.ReadFromJsonAsync<AdventureDraftResponse>())!.Markdown, Is.EqualTo(draft.Markdown));
    }

    [Test]
    public async Task TheDraft_SplitsAtItsHeadings_IntoSections_AndGathersBack()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var started = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft",
            new SaveAdventureDraftRequest("Steam.\n\n## The Baths\n\nSlimes lurk.", null));
        var draft = (await started.Content.ReadFromJsonAsync<AdventureDraftResponse>())!;

        var planned = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft/plan", new PlanDraftRequest(false));
        var plan = (await planned.Content.ReadFromJsonAsync<DraftPlanResponse>())!;
        var split = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft/split", new SplitDraftRequest(
            plan.Rows.Select(r => new DraftSplitPointRequest(r.StartLine, r.Title, r.Kind)).ToList(), "Replace", plan.DraftUpdatedAt));
        var after = (await split.Content.ReadFromJsonAsync<AdventureResponse>())!;

        Assert.Multiple(() =>
        {
            Assert.That(plan.Source, Is.EqualTo("Headings"));
            Assert.That(plan.Rows.Select(r => (r.StartLine, r.Title, r.Kind)), Is.EqualTo([(1, "Overview", "Overview"), (3, "The Baths", "Scene")]));
            Assert.That(after.Sections.Select(x => (x.Title, x.BodyMarkdown)), Is.EqualTo([("Overview", "Steam."), ("The Baths", "Slimes lurk.")]));
            Assert.That(after.Draft!.RevisionCount, Is.Zero, "the skeleton had no written sections to keep");
            Assert.That(_factory.FakeDrafting.Requests, Is.Empty, "a headings split uses no AI");
        });

        var gathered = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft/gather", new GatherDraftRequest(draft.UpdatedAt));
        Assert.That((await gathered.Content.ReadFromJsonAsync<AdventureDraftResponse>())!.Markdown,
            Is.EqualTo("## Overview\n\nSteam.\n\n## The Baths\n\nSlimes lurk."));
    }

    [Test]
    public async Task ASplitWithAnUnknownKind_IsAPlain400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/draft/split",
            new SplitDraftRequest([new DraftSplitPointRequest(1, "A", "Dungeon")], "Replace", null));

        Assert.That((await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code, Is.EqualTo("invalid_kind"));
    }

    [Test]
    public async Task AnUnknownConversationScope_IsAPlain400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("Who else?", null, "Everything"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code, Is.EqualTo("invalid_scope"));
        Assert.That(_factory.FakeDrafting.Requests, Is.Empty);
    }

    [Test]
    public async Task Gm_SetsTheTableBrief_AndSourceBooks_WhichMustBeTheWorldsBooks()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);

        var brief = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/brief",
            new SetTableBriefRequest("Four players, level 3, two hours."));
        var stranger = await scenario.GmClient.PutAsJsonAsync($"{Base(worldId)}/{adventure.Id}/source-books",
            new SetSourceBooksRequest([Guid.NewGuid()]));

        var after = await brief.Content.ReadFromJsonAsync<AdventureResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(after!.TableBrief, Is.EqualTo("Four players, level 3, two hours."));
            Assert.That(after.SourceBooks, Is.Empty);
            Assert.That(stranger.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task DeletingTheAdventure_DeletesItsConversation()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var sent = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/conversation/messages",
            new SendConversationMessageRequest("Who else?", null));
        await ReadEventsAsync(sent);

        var deleted = await scenario.GmClient.DeleteAsync($"{Base(worldId)}/{adventure.Id}");
        var conversation = await scenario.GmClient.GetAsync($"{Base(worldId)}/{adventure.Id}/conversation");

        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(conversation.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
