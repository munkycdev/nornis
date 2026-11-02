using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
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
        yield return ("delete", (c, w, a, _) => c.DeleteAsync($"{Base(w)}/{a}"));
        yield return ("save section", (c, w, a, s) => c.PutAsJsonAsync($"{Base(w)}/{a}/sections/{s}",
            new SaveAdventureSectionRequest("Notes", "Mine", "Body", DateTimeOffset.UtcNow)));
        yield return ("add section", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/sections",
            new AddAdventureSectionRequest("Notes", "Mine", null)));
        yield return ("delete section", (c, w, a, s) => c.DeleteAsync($"{Base(w)}/{a}/sections/{s}"));
        yield return ("reorder", (c, w, a, s) => c.PutAsJsonAsync($"{Base(w)}/{a}/order", new ReorderAdventureSectionsRequest([s])));
        yield return ("undo", (c, w, a, s) => c.PostAsync($"{Base(w)}/{a}/sections/{s}/undo", null));
        yield return ("draft", (c, w, a, s) => c.PostAsJsonAsync($"{Base(w)}/{a}/sections/{s}/draft",
            new DraftAdventureSectionRequest("Write it.", DateTimeOffset.UtcNow)));
        yield return ("budget", (c, w, _, _) => c.GetAsync($"/api/worlds/{w}/prep-budget"));
        yield return ("print", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/print"));
        yield return ("export", (c, w, a, _) => c.PostAsJsonAsync($"{Base(w)}/{a}/export", new ExportAdventureRequest(null)));
        yield return ("conversation", (c, w, a, _) => c.GetAsync($"{Base(w)}/{a}/conversation"));
        yield return ("send message", (c, w, a, s) => c.PostAsJsonAsync($"{Base(w)}/{a}/conversation/messages",
            new SendConversationMessageRequest("Who else could the cult have recruited?", s)));
        yield return ("clear conversation", (c, w, a, _) => c.DeleteAsync($"{Base(w)}/{a}/conversation"));
        yield return ("put in section", (c, w, a, s) => c.PostAsJsonAsync($"{Base(w)}/{a}/sections/{s}/put",
            new PutInSectionRequest(Guid.NewGuid(), "Replace", DateTimeOffset.UtcNow)));
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
        Assert.That(after.Sections.Select(s => s.Id), Is.EqualTo(adventure.Sections.Select(s => s.Id)));
        var list = await scenario.GmClient.GetFromJsonAsync<List<AdventureSummaryResponse>>(Base(worldId));
        Assert.That(list, Has.Count.EqualTo(1));
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

    // ------------------------------------------------------------------ drafting --

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
    public async Task Draft_StreamsTheText_ThenTheSavedSection_AndUndoHasOneStep()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var scene = adventure.Sections.First(s => s.Kind == "Scene");
        _factory.FakeDrafting.Text = "> Gulls wheel over the docks.";

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/sections/{scene.Id}/draft",
            new DraftAdventureSectionRequest("Open on the docks.", scene.UpdatedAt));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/event-stream"));
        var events = await ReadEventsAsync(response);
        var deltas = events.Where(e => e.EventType == DraftStreamEventNames.Delta)
            .Select(e => JsonSerializer.Deserialize<DraftStreamDeltaResponse>(e.Data, JsonSerializerOptions.Web)!.Text);
        var done = JsonSerializer.Deserialize<AdventureSectionResponse>(events[^1].Data, JsonSerializerOptions.Web)!;
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(deltas), Is.EqualTo("> Gulls wheel over the docks."));
            Assert.That(events[^1].EventType, Is.EqualTo(DraftStreamEventNames.Done));
            Assert.That(done.BodyMarkdown, Is.EqualTo("> Gulls wheel over the docks."));
            Assert.That(done.RevisionCount, Is.EqualTo(1));
            Assert.That(done.IsFilled, Is.True);
            Assert.That(_factory.FakeDrafting.Requests.Single().UserMessage, Does.Contain("Open on the docks."));
        });

        var after = await scenario.GmClient.GetFromJsonAsync<AdventureResponse>($"{Base(worldId)}/{adventure.Id}");
        Assert.That(after!.Sections.Single(s => s.Id == scene.Id).BodyMarkdown, Is.EqualTo("> Gulls wheel over the docks."));

        var undone = await scenario.GmClient.PostAsync($"{Base(worldId)}/{adventure.Id}/sections/{scene.Id}/undo", null);
        var restored = (await undone.Content.ReadFromJsonAsync<AdventureResponse>())!.Sections.Single(s => s.Id == scene.Id);
        Assert.That(restored.BodyMarkdown, Is.EqualTo(scene.BodyMarkdown));
        Assert.That(restored.RevisionCount, Is.Zero);
    }

    [Test]
    public async Task Draft_OnAStaleStamp_IsAPlain409_NotAStream()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await CreateAsync(scenario.GmClient, worldId);
        var scene = adventure.Sections.First(s => s.Kind == "Scene");

        var response = await scenario.GmClient.PostAsJsonAsync($"{Base(worldId)}/{adventure.Id}/sections/{scene.Id}/draft",
            new DraftAdventureSectionRequest(null, scene.UpdatedAt.AddMinutes(-1)));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code, Is.EqualTo("section_changed"));
        Assert.That(_factory.FakeDrafting.Requests, Is.Empty);
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
    public async Task Print_ReturnsTheBooklet_WithTheTitleItWillBeFiledUnder()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var worldId = scenario.World.Id;
        var adventure = await WriteOverviewAsync(scenario.GmClient, worldId,
            await CreateAsync(scenario.GmClient, worldId), "> The vault door is cold.");

        var print = await scenario.GmClient.GetFromJsonAsync<AdventurePrintResponse>($"{Base(worldId)}/{adventure.Id}/print");

        Assert.Multiple(() =>
        {
            Assert.That(print!.AdventureId, Is.EqualTo(adventure.Id));
            Assert.That(print.Html, Does.StartWith("<article class=\"booklet\">"));
            Assert.That(print.Html, Does.Contain("<blockquote class=\"read-aloud\">"));
            Assert.That(print.LibraryTitle, Does.StartWith("The Silver Key, printed "));
        });
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
