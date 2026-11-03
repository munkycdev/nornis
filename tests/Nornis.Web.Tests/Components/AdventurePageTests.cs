using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Adventure page (feature 29): who gets it, what the outline and toolbar show, and the two
/// requests whose shape matters, a save based on the version it was opened at and a reorder
/// that names every section.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AdventurePageTests : BunitContext
{
    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(new BreadcrumbState());
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<string>("nornisEditor.getHtml", _ => true).SetResult("<p>The key opens the vault.</p>");
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<AdventureDetail>> RenderAsync()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        return Render<AdventureDetail>(p => p.Add(x => x.Key, "the-silver-key"));
    }

    [Test]
    public async Task APlayer_SeesGmsOnly_AndThePageNeverAsksForTheAdventure()
    {
        _handler.Role = "Player";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("GMs only")));
        Assert.That(_handler.Requests, Has.None.Contains("/prep"));
        Assert.That(cut.Markup, Does.Not.Contain("Captain Voss"));
    }

    [Test]
    public async Task TheGm_SeesTheOutline_AndThePrepToolbar()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("The Silver Key"));
            Assert.That(cut.Markup, Does.Contain("Overview"));
            Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks"));
            Assert.That(cut.Markup, Does.Contain("aria-label=\"Read-aloud\""));
            Assert.That(cut.Markup, Does.Contain("aria-label=\"Prop\""));
            Assert.That(cut.Markup, Does.Contain("aria-label=\"Ask\""));
            Assert.That(cut.Markup, Does.Contain("aria-label=\"Stat block\""));
            Assert.That(cut.Markup, Does.Not.Contain("aria-label=\"Quote\""), "Read-aloud takes Quote's place");
        }));
    }

    [Test]
    public async Task Save_IsBasedOnTheVersionOpened_AndAConflictSaysNothingWasOverwritten()
    {
        _handler.SaveStatus = HttpStatusCode.Conflict;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks")));

        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("changed somewhere else")));
        var save = JsonDocument.Parse(_handler.LastSaveBody!).RootElement;
        Assert.That(save.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(), Is.EqualTo(StubHandler.OverviewStamp));
        Assert.That(save.GetProperty("bodyMarkdown").GetString(), Is.EqualTo("The key opens the vault."));
    }

    [Test]
    public async Task MovingASectionDown_SendsEverySectionInTheNewOrder()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks")));

        await cut.InvokeAsync(() => cut.FindAll("button[aria-label='Move down']").First().Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastReorderBody, Is.Not.Null));
        var ids = JsonDocument.Parse(_handler.LastReorderBody!).RootElement.GetProperty("sectionIds")
            .EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.That(ids, Is.EqualTo([StubHandler.SceneId, StubHandler.OverviewId]));
    }

    [Test]
    public async Task Revise_SavesWhatWasTypedFirst_ThenDraftsFromThatVersion_AndShowsTheResult()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks")));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-draft textarea").Input("Make it tense."));
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Revise").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Undo (1)")));
        var draft = JsonDocument.Parse(_handler.LastDraftBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Requests.FindIndex(r => r.StartsWith("PUT", StringComparison.Ordinal) && r.Contains("/sections/")),
                Is.LessThan(_handler.Requests.FindIndex(r => r.EndsWith("/draft", StringComparison.Ordinal))),
                "what was typed is saved before the draft starts");
            Assert.That(draft.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(), Is.EqualTo(StubHandler.SavedStamp),
                "the draft is based on the version the save just wrote");
            Assert.That(draft.GetProperty("instruction").GetString(), Is.EqualTo("Make it tense."));
            Assert.That(cut.FindAll(".nornis-prep-draft-preview"), Is.Empty, "a saved draft is in the editor, not the preview");
        });
    }

    [Test]
    public async Task AFailedDraft_SaysNothingWasSaved_AndKeepsWhatWasWrittenToRead()
    {
        _handler.DraftFails = true;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks")));

        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Revise").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Drafting is temporarily unavailable")));
        Assert.That(cut.Find(".nornis-prep-draft-preview").TextContent, Does.Contain("> Gulls wheel"));
        Assert.That(cut.Markup, Does.Not.Contain("Undo (1)"));
    }

    [Test]
    public async Task Export_FilesTheChapterChosen_AndLinksToTheSourceAndOnToReview()
    {
        _handler.WithChapter = true;
        var cut = await RenderAsync();
        var dialogs = Render<MudDialogProvider>();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Chapter 1: The Heist")));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-export").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("The whole adventure")));
        await dialogs.InvokeAsync(() => dialogs.FindAll(".nornis-prep-export-choice input[type=radio]")[1].Click());
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Export").Click());

        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Nornis is reading it now")));
        var body = JsonDocument.Parse(_handler.LastExportBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("chapterSectionId").GetGuid(), Is.EqualTo(StubHandler.ChapterId));
            Assert.That(dialogs.Find(".nornis-prep-export-done a[href^='/sources/']").GetAttribute("href"),
                Is.EqualTo("/sources/the-silver-key-the-heist-prep"));
            Assert.That(dialogs.FindAll(".nornis-prep-export-done a[href='/review']"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Print_SavesWhatWasTyped_ThenOpensTheBooklet()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks")));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-print").Click());

        var nav = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.That(nav.Uri, Does.EndWith("/prep/the-silver-key/print")));
        Assert.That(_handler.LastSaveBody, Does.Contain("The key opens the vault."), "the booklet prints what is on screen");
    }

    // ------------------------------------------------------- Talk it through --

    [Test]
    public async Task TheGm_SeesTalkItThrough_WithTheSavedConversation()
    {
        _handler.ConversationJson = StubHandler.Answered("Who else could the cult have recruited?", "The harbormaster.");

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-talk").TextContent, Does.Contain("Talk it through"));
            Assert.That(cut.Find(".nornis-prep-talk-question").TextContent, Does.Contain("Who else could the cult have recruited?"));
            Assert.That(cut.Find(".nornis-prep-talk-answer").TextContent, Does.Contain("The harbormaster."));
            Assert.That(cut.FindAll(".nornis-prep-talk-put"), Has.Count.EqualTo(1), "only an answer can be put in");
            Assert.That(cut.FindAll(".nornis-prep-talk-unanswered"), Is.Empty);
        }));
    }

    [Test]
    public async Task Send_StreamsTheAnswer_WithTheOpenSection_ThenShowsTheSavedConversation()
    {
        _handler.ConversationAfterSendJson = StubHandler.Answered("Who else?", "Three cultists.");
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Talk it through")));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Who else?"));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-send").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-put"), Has.Count.EqualTo(1)));
        var sent = JsonDocument.Parse(_handler.LastSendBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(sent.GetProperty("text").GetString(), Is.EqualTo("Who else?"));
            Assert.That(sent.GetProperty("sectionId").GetGuid(), Is.EqualTo(StubHandler.OverviewId), "the section on screen rides along");
            Assert.That(cut.Find(".nornis-prep-talk-answer").TextContent, Does.Contain("Three cultists."));
            Assert.That(cut.FindAll(".nornis-prep-talk-error"), Is.Empty);
        });
    }

    [Test]
    public async Task AFailedAnswer_SaysItWasNotSaved_UnderTheQuestion()
    {
        _handler.SendFails = true;
        _handler.ConversationAfterSendJson = """
            {"messages":[{"id":"7b0f1c3e-1111-4c4c-9c9c-000000000001","role":"User","content":"Who else?","model":null,"createdAt":"2026-11-02T10:30:00+00:00"}],"isFull":false}
            """;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Talk it through")));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Who else?"));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-send").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-unanswered"), Has.Count.EqualTo(1)));
        Assert.That(cut.Find(".nornis-prep-talk-error").TextContent, Does.Contain("wasn't saved"));
        Assert.That(cut.FindAll(".nornis-prep-talk-put"), Is.Empty);
    }

    [Test]
    public async Task PutInSection_SavesTheEditorFirst_ThenSendsTheNewStampAndTheMode()
    {
        _handler.ConversationJson = StubHandler.Answered("Who else?", "Three cultists.");
        var cut = await RenderAsync();
        var dialogs = Render<MudDialogProvider>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-put"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-put").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Add it after what's there")));
        await dialogs.InvokeAsync(() => dialogs.Find("button.nornis-prep-put-submit").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastPutBody, Is.Not.Null));
        var put = JsonDocument.Parse(_handler.LastPutBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastPutPath, Does.EndWith($"/sections/{StubHandler.OverviewId}/put"), "the open section is offered first");
            Assert.That(put.GetProperty("messageId").GetGuid(), Is.EqualTo(StubHandler.AnswerId));
            Assert.That(put.GetProperty("mode").GetString(), Is.EqualTo(PutInSectionRequest.Append));
            Assert.That(put.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(), Is.EqualTo(StubHandler.SavedStamp),
                "based on the version the editor's save just wrote, so the GM's typing isn't put under");
            Assert.That(_handler.LastSaveBody, Does.Contain("The key opens the vault."));
        });
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Undo (1)")));
    }

    [Test]
    public async Task Clear_AsksFirst_AndKeepsTheConversationIfTheGmSaysNo()
    {
        _handler.ConversationJson = StubHandler.Answered("Who else?", "Three cultists.");
        var cut = await RenderAsync();
        var dialogs = Render<MudDialogProvider>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-prep-talk-clear"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-clear").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Clear the conversation?")));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Keep it").Click());
        Assert.That(_handler.Cleared, Is.False);

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-clear").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Clear the conversation?")));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Clear").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.Cleared, Is.True));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-message"), Is.Empty));
    }

    // ---------------------------------------------------------- Threads to use --

    [Test]
    public async Task TheOverview_ShowsTheGaugesTopFive_WithTheirReasons()
    {
        _handler.ConvergenceJson = StubHandler.Gauge(Enumerable.Range(1, 7)
            .Select(i => ($"Anchor {i}", $"Secret number {i}.")).ToArray());

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-prep-thread"), Has.Count.EqualTo(ThreadsToUseCard.MaxThreads));
            Assert.That(cut.Find(".nornis-prep-threads").TextContent, Does.Contain("Anchor 1"));
            Assert.That(cut.Find(".nornis-prep-threads").TextContent, Does.Not.Contain("Anchor 6"));
            Assert.That(cut.Find(".nornis-prep-thread").TextContent, Does.Contain("hidden for 32 days"), "the gauge's own reasons");
        }));
        Assert.That(_handler.Requests, Has.None.Contains("/narrate"), "threads to use never spend");
    }

    [Test]
    public async Task UseInThisAdventure_AddsTheThreadToTheInstruction_OnceAndAfterWhatWasTyped()
    {
        _handler.ConvergenceJson = StubHandler.Gauge(("Captain Voss", "Voss is the smuggler king."));
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-prep-thread-use"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-draft textarea").Input("Keep it short."));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-thread-use").Click());
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-thread-use").Click());
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Revise").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastDraftBody, Is.Not.Null));
        var instruction = JsonDocument.Parse(_handler.LastDraftBody!).RootElement.GetProperty("instruction").GetString();
        Assert.Multiple(() =>
        {
            Assert.That(instruction, Is.EqualTo("Keep it short.\nBuild toward this hidden thread: Captain Voss: Voss is the smuggler king."));
            Assert.That(_handler.LastSaveBody, Does.Not.Contain("smuggler king"), "the thread goes to the instruction, never a section");
        });
    }

    [Test]
    public async Task AWorldWithNothingHidden_SaysSoInOneLine()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-prep-threads-empty").TextContent, Does.Contain("Nothing hidden yet")));
        Assert.That(cut.FindAll(".nornis-prep-thread"), Is.Empty);
    }

    [Test]
    public async Task AScene_HasNoThreadsCard()
    {
        _handler.ConvergenceJson = StubHandler.Gauge(("Captain Voss", "Voss is the smuggler king."));
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-threads"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.FindAll("button.nornis-prep-outline-title").First(b => b.TextContent.Contains("Scene 1")).Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-threads"), Is.Empty));
    }

    [Test]
    public async Task TheBookDetails_SitUnderTheOutline()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-side .nornis-book-details"), Has.Count.EqualTo(1)));
        Assert.That(cut.Find(".nornis-book-details").TextContent, Does.Contain("From the world"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public static readonly Guid OverviewId = Guid.NewGuid();
        public static readonly Guid SceneId = Guid.NewGuid();
        public static readonly Guid ChapterId = Guid.NewGuid();
        public static readonly DateTimeOffset OverviewStamp = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

        private readonly Guid _worldId = Guid.NewGuid();
        private readonly Guid _adventureId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public static readonly DateTimeOffset SavedStamp = new(2026, 11, 2, 11, 0, 0, TimeSpan.Zero);

        public HttpStatusCode SaveStatus { get; set; } = HttpStatusCode.OK;
        public bool DraftFails { get; set; }
        public string? LastDraftBody { get; private set; }
        public List<string> Requests { get; } = [];
        public string? LastSaveBody { get; private set; }
        public string? LastReorderBody { get; private set; }
        public string? LastExportBody { get; private set; }

        /// <summary>Adds a Chapter after the two sections, for the export's choice.</summary>
        public bool WithChapter { get; set; }

        public static readonly Guid AnswerId = Guid.NewGuid();

        /// <summary>What GET conversation answers; the empty conversation until a test says otherwise.</summary>
        public string ConversationJson { get; set; } = """{"messages":[],"isFull":false}""";

        /// <summary>What GET conversation answers once a message has been sent.</summary>
        public string? ConversationAfterSendJson { get; set; }

        /// <summary>What GET convergence answers; a world with nothing hidden until a test says otherwise.</summary>
        public string ConvergenceJson { get; set; } = Gauge();

        public bool SendFails { get; set; }
        public string? LastSendBody { get; private set; }
        public string? LastPutBody { get; private set; }
        public string? LastPutPath { get; private set; }
        public bool Cleared { get; private set; }

        /// <summary>A gauge of these candidates, strongest first: (anchor, description) each.</summary>
        public static string Gauge(params (string Anchor, string Description)[] candidates)
        {
            var rows = candidates.Select((c, i) => $$"""
                {"kind":"Fact","id":"{{Guid.NewGuid()}}","anchorArtifactId":"{{Guid.NewGuid()}}","anchorName":"{{c.Anchor}}",
                 "description":"{{c.Description}}","createdAt":"2026-10-01T00:00:00+00:00","missingArtifactIds":[],
                 "components":{"daysHidden":32,"partyVisibleFactsOnAnchor":2,"missingArtifactCount":0,"isSelfContained":true,
                   "storylineStatus":null,"contradictionSeverity":null,"contradictionAssessed":false,"dormancy":0.5,
                   "anchorFamiliarity":0.5,"selfContainment":1.0,"storylineState":0.0,"contradictionPressure":null},
                 "score":{{90 - i}},"rationale":null}
                """);
            return $$"""
                {"worldId":"{{Guid.NewGuid()}}","generatedAt":"2026-11-02T10:00:00+00:00","assessmentId":null,
                 "totalCandidates":{{candidates.Length}},"candidates":[{{string.Join(",", rows)}}]}
                """;
        }

        public static string Answered(string question, string answer) => $$"""
            {"messages":[
              {"id":"{{Guid.NewGuid()}}","role":"User","content":"{{question}}","model":null,"createdAt":"2026-11-02T10:30:00+00:00"},
              {"id":"{{AnswerId}}","role":"Assistant","content":"{{answer}}","model":"nornis-ask","createdAt":"2026-11-02T10:30:05+00:00"}],
             "isFull":false}
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");

            if (path == "/api/worlds")
            {
                return Json($$"""
                    [{"id":"{{_worldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"{{Role}}"}]
                    """);
            }

            if (path.EndsWith("/convergence", StringComparison.Ordinal))
            {
                return Json(ConvergenceJson);
            }

            if (path.EndsWith("/campaigns", StringComparison.Ordinal))
            {
                return Json("[]");
            }

            if (request.Method == HttpMethod.Put && path.EndsWith($"/sections/{OverviewId}", StringComparison.Ordinal))
            {
                LastSaveBody = await request.Content!.ReadAsStringAsync(ct);
                return SaveStatus == HttpStatusCode.Conflict
                    ? new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = new StringContent("""{"code":"section_changed","message":"This section changed since you opened it."}""",
                            Encoding.UTF8, "application/json"),
                    }
                    : Json($$"""
                        {"id":"{{OverviewId}}","kind":"Overview","title":"Overview","bodyMarkdown":"The key opens the vault.",
                         "sortOrder":1,"updatedAt":"2026-11-02T11:00:00+00:00","isFilled":true}
                        """);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/draft", StringComparison.Ordinal))
            {
                LastDraftBody = await request.Content!.ReadAsStringAsync(ct);
                var end = DraftFails
                    ? "event: error\ndata: {\"code\":\"service_unavailable\",\"message\":\"Drafting is temporarily unavailable. Nothing was changed. Please try again.\"}\n\n"
                    : $"event: done\ndata: {{\"id\":\"{OverviewId}\",\"kind\":\"Overview\",\"title\":\"Overview\",\"bodyMarkdown\":\"> Gulls wheel over the docks.\",\"sortOrder\":1,\"updatedAt\":\"2026-11-02T11:05:00+00:00\",\"revisionCount\":1,\"isFilled\":true}}\n\n";
                var stream = "event: delta\ndata: {\"text\":\"> Gulls wheel \"}\n\n" + end;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8) };
                response.Content.Headers.ContentType = new("text/event-stream");
                return response;
            }

            if (path.EndsWith("/conversation", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Delete)
                {
                    Cleared = true;
                    ConversationJson = """{"messages":[],"isFull":false}""";
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                return Json(ConversationJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/conversation/messages", StringComparison.Ordinal))
            {
                LastSendBody = await request.Content!.ReadAsStringAsync(ct);
                ConversationJson = ConversationAfterSendJson ?? ConversationJson;
                var end = SendFails
                    ? "event: error\ndata: {\"code\":\"answer_cut_off\",\"message\":\"The answer ran past the length one answer can be and was cut off, so it wasn't saved.\"}\n\n"
                    : $"event: done\ndata: {{\"id\":\"{AnswerId}\",\"role\":\"Assistant\",\"content\":\"Three cultists.\",\"model\":\"nornis-ask\",\"createdAt\":\"2026-11-02T10:30:05+00:00\"}}\n\n";
                var stream = "event: delta\ndata: {\"text\":\"Three \"}\n\n" + end;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(stream, Encoding.UTF8) };
                response.Content.Headers.ContentType = new("text/event-stream");
                return response;
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/put", StringComparison.Ordinal))
            {
                LastPutBody = await request.Content!.ReadAsStringAsync(ct);
                LastPutPath = path;
                return Json($$"""
                    {"id":"{{OverviewId}}","kind":"Overview","title":"Overview","bodyMarkdown":"The key opens the vault.\n\nThree cultists.",
                     "sortOrder":1,"updatedAt":"2026-11-02T11:10:00+00:00","revisionCount":1,"isFilled":true}
                    """);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/export", StringComparison.Ordinal))
            {
                LastExportBody = await request.Content!.ReadAsStringAsync(ct);
                return Json($$"""
                    {"sourceId":"{{Guid.NewGuid()}}","title":"The Silver Key: The Heist (prep)","slug":"the-silver-key-the-heist-prep"}
                    """);
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/order", StringComparison.Ordinal))
            {
                LastReorderBody = await request.Content!.ReadAsStringAsync(ct);
                return Json(Adventure(SceneId, OverviewId));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/prep/", StringComparison.Ordinal))
            {
                return Json(Adventure(OverviewId, SceneId));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string Adventure(Guid first, Guid second)
        {
            string Section(Guid id, int order) => id == OverviewId
                ? $$"""{"id":"{{id}}","kind":"Overview","title":"Overview","bodyMarkdown":"Captain Voss hides the key.","sortOrder":{{order}},"updatedAt":"{{OverviewStamp:O}}","revisionCount":0,"isFilled":true}"""
                : $$"""{"id":"{{id}}","kind":"Scene","title":"Scene 1: The Docks","bodyMarkdown":"","sortOrder":{{order}},"updatedAt":"2026-11-02T10:00:00+00:00","revisionCount":0,"isFilled":false}""";

            return $$"""
                {"id":"{{_adventureId}}","worldId":"{{_worldId}}","campaignId":null,"title":"The Silver Key",
                 "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                 "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-02T10:00:00+00:00",
                 "sections":[{{Section(first, 1)}},{{Section(second, 2)}}{{Chapter()}}],
                 "details":{"gameSystem":null,"edition":null,"levels":null,"year":null},
                 "printDetails":{"title":"The Silver Key","gameSystem":"Fate Accelerated","edition":null,
                   "type":"Nornis adventure","levels":null,"publisher":"Nornis Press","author":"kelda","year":null} }
                """;
        }

        private string Chapter() => WithChapter
            ? $$""",{"id":"{{ChapterId}}","kind":"Chapter","title":"Chapter 1: The Heist","bodyMarkdown":"","sortOrder":3,"updatedAt":"2026-11-02T10:00:00+00:00","revisionCount":0,"isFilled":false}"""
            : string.Empty;

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
