using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    public async Task TheDocumentsCard_ListsWhatBelongsToThisAdventure_AndNothingElse()
    {
        var other = Guid.NewGuid();
        _handler.LibraryJson = JsonSerializer.Serialize(new[]
        {
            LibraryDoc("Vault map", "Map", linked: _handler.AdventureId, print: null, onShelf: false),
            LibraryDoc("The Silver Key (print)", "Print", linked: null, print: _handler.AdventureId, onShelf: true),
            LibraryDoc("Another one's handout", "Handout", linked: other, print: null, onShelf: false),
            LibraryDoc("Player's Guide", "Book", linked: null, print: null, onShelf: true),
        }, JsonSerializerOptions.Web);

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(
            cut.FindAll(".nornis-adventure-document .nornis-list-title").Select(t => t.TextContent.Trim()),
            Is.EquivalentTo((string[])["Vault map", "The Silver Key (print)"])));
    }

    [Test]
    public async Task TheDocumentsCard_SaysHowToAddOne_WhenNothingBelongs()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-adventure-documents-empty").TextContent,
            Does.Contain("Nothing belongs to this adventure yet")));
    }

    private static LibraryDocumentDto LibraryDoc(string title, string category, Guid? linked, Guid? print, bool onShelf) => new(
        Guid.NewGuid(), Guid.NewGuid(), title, "file.png", "image/png", 1024, category == "Book" ? "Sourcebook" : "Map",
        "GMOnly", "Stored", null, 0, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        AdventureId: print, Category: category, LinkedAdventureId: linked, OnShelf: onShelf);

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
    public async Task ASavesBlockWarnings_ShowUnderTheEditor_AndGoWhenFixed()
    {
        _handler.SaveWarningsJson = """["The first stat block has no name, so Export to Nornis can't file it on anyone. Start it with \"# Name\"."]""";
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Scene 1: The Docks")));
        Assert.That(cut.FindAll(".nornis-prep-block-warnings"), Is.Empty, "a section with no warnings shows no alert");

        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click());
        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-prep-block-warnings").TextContent,
            Does.Contain("The first stat block has no name")));

        _handler.SaveWarningsJson = "[]";
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Trim() == "Save").Click());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-block-warnings"), Is.Empty));
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

    // ------------------------------------------------------------- web research --

    private const string ResearchAvailable = "\"webResearch\":{\"available\":true,\"reason\":null}";

    [Test]
    public async Task WebResearch_WhenTheKeyCantSearch_IsOff_Disabled_AndSaysWhy()
    {
        _handler.ConversationJson = """
            {"messages":[],"isFull":false,"webResearch":{"available":false,"reason":"Web research runs on your own AI key. Add one under Profile, AI keys."}}
            """;

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-talk-research input").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find(".nornis-prep-talk-research-reason").TextContent,
                Is.EqualTo("Web research runs on your own AI key. Add one under Profile, AI keys."));
        }));
    }

    [Test]
    public async Task WebResearch_RidesWithOneTurn_ThenTurnsItselfOff()
    {
        _handler.ConversationJson = "{\"messages\":[],\"isFull\":false," + ResearchAvailable + "}";
        _handler.ConversationAfterSendJson = StubHandler.Answered("Waterdeep?", "Wards.").TrimEnd()[..^1] + "," + ResearchAvailable + "}";
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-prep-talk-research input").HasAttribute("disabled"), Is.False));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-research input").Change(true));
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Waterdeep?"));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-send").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-put"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(JsonDocument.Parse(_handler.LastSendBody!).RootElement.GetProperty("webResearch").GetBoolean(), Is.True);
            Assert.That(cut.Find(".nornis-prep-talk-research input").HasAttribute("checked"), Is.False,
                "off again after the send, so the next search is a choice too");
        });
    }

    [Test]
    public async Task ATurnWithoutTheSwitch_DoesNotAskToSearch()
    {
        _handler.ConversationJson = "{\"messages\":[],\"isFull\":false," + ResearchAvailable + "}";
        _handler.ConversationAfterSendJson = StubHandler.Answered("Who else?", "Three cultists.");
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Talk it through")));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Who else?"));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-send").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastSendBody, Is.Not.Null));
        Assert.That(JsonDocument.Parse(_handler.LastSendBody!).RootElement.GetProperty("webResearch").GetBoolean(), Is.False);
    }

    [Test]
    public async Task AResearchAnswer_ListsItsPages_FromTheWeb_AndShowsSuggestionsSandboxed()
    {
        _handler.ConversationJson = $$$"""
            {"messages":[
              {"id":"{{{Guid.NewGuid()}}}","role":"User","content":"Waterdeep?","model":null,"createdAt":"2026-11-02T10:30:00+00:00"},
              {"id":"{{{StubHandler.AnswerId}}}","role":"Assistant","content":"Wards.","model":"nornis-ask","createdAt":"2026-11-02T10:30:05+00:00",
               "webResearch":{"pages":[{"title":"Wards of Waterdeep","url":"https://example.org/wards"}],"searches":2,
                              "suggestionsHtml":"<a href=\"https://www.google.com/search?q=waterdeep\">waterdeep</a><script>alert(1)</script>"}},
              {"id":"{{{Guid.NewGuid()}}}","role":"User","content":"And Neverwinter?","model":null,"createdAt":"2026-11-02T10:31:00+00:00"},
              {"id":"{{{Guid.NewGuid()}}}","role":"Assistant","content":"The record says.","model":"nornis-ask","createdAt":"2026-11-02T10:31:05+00:00",
               "webResearch":{"pages":[],"searches":0,"suggestionsHtml":null}}],
             "isFull":false}
            """;

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-web"), Has.Count.EqualTo(2)));
        var link = cut.Find(".nornis-prep-talk-web-pages a");
        var frame = cut.Find("iframe.nornis-prep-talk-web-suggestions");
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-prep-talk-web")[0].TextContent, Does.Contain("From the web"));
            Assert.That(link.TextContent, Is.EqualTo("Wards of Waterdeep"));
            Assert.That(link.GetAttribute("href"), Is.EqualTo("https://example.org/wards"));
            Assert.That(link.GetAttribute("target"), Is.EqualTo("_blank"));
            Assert.That(link.GetAttribute("rel"), Is.EqualTo("noopener noreferrer nofollow"));
            Assert.That(frame.GetAttribute("sandbox"), Does.Not.Contain("allow-scripts").And.Not.Contain("allow-same-origin"),
                "the provider's markup can't run script or reach Nornis's page");
            Assert.That(frame.GetAttribute("srcdoc"), Does.Contain("<base target=\"_blank\">").And.Contain("waterdeep</a>"));
            Assert.That(cut.Find(".nornis-prep-talk-web-none").TextContent, Is.EqualTo("Searched the web; no pages cited."));
            Assert.That(cut.FindAll(".nornis-prep-talk-web-suggestions"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Enter_SendsTheMessage()
    {
        _handler.ConversationAfterSendJson = StubHandler.Answered("Who else?", "Three cultists.");
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Talk it through")));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Who else?"));
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").KeyDown(new KeyboardEventArgs { Key = "Enter" }));

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-put"), Has.Count.EqualTo(1)));
        Assert.That(JsonDocument.Parse(_handler.LastSendBody!).RootElement.GetProperty("text").GetString(), Is.EqualTo("Who else?"));
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task ShiftEnter_OrEnterWhileComposing_DoesNotSend(bool shift, bool composing)
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Talk it through")));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Who else?"));
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea")
            .KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = shift, IsComposing = composing }));

        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastSendBody, Is.Null, "nothing was sent");
            Assert.That(cut.Find("button.nornis-prep-talk-send").HasAttribute("disabled"), Is.False, "the message is still there to send");
        });
    }

    [Test]
    public async Task TheMessageBox_IsMarkedForEnterToSend()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(
            cut.Find(".nornis-prep-talk-input textarea").Closest("[data-submit-on-enter]"), Is.Not.Null,
            "nornis-compose.js stops Enter's newline only inside a [data-submit-on-enter] box"));
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

    // ------------------------------------------------------------ Design mode (feature 37) --

    private static IElement ModeButton(IRenderedComponent<AdventureDetail> cut, string label) =>
        cut.FindAll(".nornis-prep-mode button").First(b => b.TextContent.Trim() == label);

    [Test]
    public async Task AnAdventureWithAWrittenSection_OpensOnSections()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-draft"), Has.Count.EqualTo(1)));
        Assert.That(cut.FindAll(".nornis-prep-design"), Is.Empty);
    }

    [Test]
    public async Task AnAdventureWithNothingWritten_OpensOnDesign_AndSaysTheDraftNeverPrints()
    {
        _handler.NoneWritten = true;

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-design-empty").TextContent, Does.Contain("never prints"));
            Assert.That(cut.FindAll(".nornis-prep-draft"), Is.Empty, "the section editor isn't on the page");
        });
    }

    [Test]
    public async Task SaveDraft_IsBasedOnTheDraftsStamp_AndAConflictSaysNothingWasOverwritten()
    {
        _handler.DraftJson = $$"""{"markdown":"Old draft.","updatedAt":"{{StubHandler.DraftStamp:O}}","revisionCount":0}""";
        _handler.DraftSaveStatus = HttpStatusCode.Conflict;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-mode button"), Has.Count.EqualTo(2)));

        await cut.InvokeAsync(() => ModeButton(cut, "Design").Click());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design"), Has.Count.EqualTo(1)));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-design-save").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("The draft changed somewhere else")));
        var save = JsonDocument.Parse(_handler.LastDraftSaveBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(save.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(), Is.EqualTo(StubHandler.DraftStamp));
            Assert.That(save.GetProperty("markdown").GetString(), Is.EqualTo("The key opens the vault."));
        });
    }

    [Test]
    public async Task AFirstDraftSave_IsBasedOnNothing()
    {
        _handler.NoneWritten = true;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-design-save").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastDraftSaveBody, Is.Not.Null));
        var save = JsonDocument.Parse(_handler.LastDraftSaveBody!).RootElement;
        Assert.That(save.GetProperty("basedOnUpdatedAt").ValueKind, Is.EqualTo(JsonValueKind.Null));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design-empty"), Is.Empty, "a saved draft is no longer \"no draft yet\""));
    }

    [Test]
    public async Task LeavingDesign_SavesWhatWasTypedFirst()
    {
        _handler.DraftJson = $$"""{"markdown":"Old draft.","updatedAt":"{{StubHandler.DraftStamp:O}}","revisionCount":0}""";
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-mode button"), Has.Count.EqualTo(2)));
        await cut.InvokeAsync(() => ModeButton(cut, "Design").Click());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => ModeButton(cut, "Sections").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-draft"), Has.Count.EqualTo(1)));
        Assert.That(JsonDocument.Parse(_handler.LastDraftSaveBody!).RootElement.GetProperty("markdown").GetString(),
            Is.EqualTo("The key opens the vault."));
    }

    [Test]
    public async Task UndoOnTheDraft_IsOfferedOnlyWhenARevisionIsKept_AndStepsBack()
    {
        _handler.DraftJson = $$"""{"markdown":"The Loremaster's.","updatedAt":"{{StubHandler.DraftStamp:O}}","revisionCount":2}""";
        _handler.NoneWritten = true;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Find("button.nornis-prep-design-undo").TextContent, Does.Contain("Undo (2)")));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-design-undo").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.DraftUndone, Is.True));
        cut.WaitForAssertion(() => Assert.That(cut.Find("button.nornis-prep-design-undo").HasAttribute("disabled"), Is.True));
    }

    [Test]
    public async Task TheTableBrief_IsEditedBesideTheBookDetails_AndSavedWhole()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-side .nornis-table-brief"), Has.Count.EqualTo(1)));
        Assert.That(cut.Find(".nornis-table-brief-empty").TextContent, Does.Contain("It never prints"));

        await cut.InvokeAsync(() => cut.Find("button.nornis-table-brief-pencil").Click());
        await cut.InvokeAsync(() => cut.Find(".nornis-table-brief textarea").Input("Four players at level 3."));
        await cut.InvokeAsync(() => cut.Find("button.nornis-table-brief-save").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-table-brief-text").TextContent, Is.EqualTo("Four players at level 3.")));
        Assert.That(JsonDocument.Parse(_handler.LastBriefBody!).RootElement.GetProperty("brief").GetString(),
            Is.EqualTo("Four players at level 3."));
    }

    [Test]
    public async Task SourceBooks_OffersOnlyTheLibrarysBooks_UpToFive()
    {
        var books = Enumerable.Range(1, 6).Select(i => LibraryDoc($"Book {i}", "Book", null, null, true)).ToList();
        _handler.LibraryJson = JsonSerializer.Serialize(
            books.Append(LibraryDoc("Harbor map", "Map", null, null, true)), JsonSerializerOptions.Web);
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-source-books-empty").TextContent, Does.Contain("every book")));

        await cut.InvokeAsync(() => cut.Find("button.nornis-source-books-pencil").Click());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-source-books-choice"), Has.Count.EqualTo(6), "the map isn't offered"));
        foreach (var i in Enumerable.Range(0, 5))
        {
            await cut.InvokeAsync(() => cut.FindAll(".nornis-source-books-choice input")[i].Change(true));
        }

        Assert.That(cut.FindAll(".nornis-source-books-choice input")[5].HasAttribute("disabled"), Is.True, "a sixth can't be chosen");
        await cut.InvokeAsync(() => cut.Find("button.nornis-source-books-save").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-source-book"), Has.Count.EqualTo(5)));
        var ids = JsonDocument.Parse(_handler.LastSourceBooksBody!).RootElement.GetProperty("documentIds")
            .EnumerateArray().Select(e => e.GetGuid()).ToList();
        Assert.That(ids, Is.EqualTo(books.Take(5).Select(b => b.Id).ToList()));
    }

    private const string EditAnswerJson = """
        {"messages":[
          {"id":"11111111-1111-1111-1111-111111111111","role":"User","content":"Louder.","model":null,"createdAt":"2026-11-03T09:00:00+00:00"},
          {"id":"22222222-2222-2222-2222-222222222222","role":"Assistant","content":"Make it louder.\n\n```edit\n...","model":"nornis-ask",
           "createdAt":"2026-11-03T09:00:05+00:00","edits":[{"find":"The spa is quiet.","replace":"The spa hums with slime."}],"prose":"Make it louder."}],
         "isFull":false}
        """;

    private async Task<IRenderedComponent<AdventureDetail>> RenderOnDesignAsync()
    {
        _handler.DraftJson = $$"""{"markdown":"The spa is quiet.","updatedAt":"{{StubHandler.DraftStamp:O}}","revisionCount":0}""";
        _handler.NoneWritten = true;
        JSInterop.Setup<string>("nornisEditor.getHtml", _ => true).SetResult("<p>The spa is quiet.</p>");
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design"), Has.Count.EqualTo(1)));
        return cut;
    }

    [Test]
    public async Task InDesignMode_AMessageIsSentWithTheDraft_NotTheSection()
    {
        var cut = await RenderOnDesignAsync();

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Make it louder."));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-send").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastSendBody, Is.Not.Null));
        var sent = JsonDocument.Parse(_handler.LastSendBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(sent.GetProperty("scope").GetString(), Is.EqualTo("Draft"));
            Assert.That(sent.GetProperty("sectionId").ValueKind, Is.EqualTo(JsonValueKind.Null));
        });
    }

    [Test]
    public async Task ASuggestedEdit_ShowsBeforeAndAfter_AndApplySendsItsIndexOnTheDraftsStamp()
    {
        _handler.ConversationJson = EditAnswerJson;
        var cut = await RenderOnDesignAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-edit"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-edit-before").TextContent, Is.EqualTo("The spa is quiet."));
            Assert.That(cut.Find(".nornis-prep-edit-after").TextContent, Is.EqualTo("The spa hums with slime."));
            Assert.That(cut.Find(".nornis-prep-talk-answer .nornis-prep-talk-text").TextContent, Is.EqualTo("Make it louder."),
                "the prose shows, not the fence");
        });

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-edit-apply").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-edit-applied"), Has.Count.EqualTo(1)));
        var apply = JsonDocument.Parse(_handler.LastApplyBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(apply.GetProperty("messageId").GetGuid(), Is.EqualTo(Guid.Parse("22222222-2222-2222-2222-222222222222")));
            Assert.That(apply.GetProperty("index").GetInt32(), Is.Zero);
            Assert.That(apply.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(), Is.EqualTo(StubHandler.DraftStamp));
        });
        cut.WaitForAssertion(() => Assert.That(cut.Find("button.nornis-prep-design-undo").TextContent, Does.Contain("Undo (1)")));
    }

    private const string WroteAnswerJson = """
        {"messages":[
          {"id":"11111111-1111-1111-1111-111111111111","role":"User","content":"Louder.","model":null,"createdAt":"2026-11-03T09:00:00+00:00"},
          {"id":"22222222-2222-2222-2222-222222222222","role":"Assistant","content":"Made it louder.\n\n````edit\n...","model":"nornis-ask",
           "createdAt":"2026-11-03T09:00:05+00:00","edits":[{"find":"The spa is quiet.","replace":"The spa hums with slime."}],"prose":"Made it louder.",
           "draftEditOutcomes":["Applied"]}],
         "isFull":false}
        """;

    [Test]
    public async Task AnAnswerThatWroteIntoTheDraft_SaysSoInALine_AndShowsItsChangesOnlyWhenAsked()
    {
        _handler.ConversationJson = WroteAnswerJson;
        var cut = await RenderOnDesignAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-talk-wrote-summary"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-talk-wrote-summary").TextContent.Trim(), Is.EqualTo("Changed the draft. Undo on the draft takes it back."));
            Assert.That(cut.FindAll(".nornis-prep-edit"), Is.Empty, "an applied edit's text stays out of the chat");
            Assert.That(cut.FindAll(".nornis-prep-talk-put-draft"), Is.Empty, "an answer that wrote into the draft isn't put in again");
        });

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-show-changes").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-edit"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-edit-after").TextContent, Is.EqualTo("The spa hums with slime."));
            Assert.That(cut.FindAll(".nornis-prep-edit-applied"), Has.Count.EqualTo(1), "it shows as applied, with nothing to press");
            Assert.That(cut.Find(".nornis-prep-edit-label").TextContent.Trim(), Is.EqualTo("Change"), "an applied change isn't a suggestion");
            Assert.That(cut.Find("button.nornis-prep-talk-show-changes").TextContent.Trim(), Is.EqualTo("Hide changes"));
        });
    }

    [Test]
    public async Task ADesignTurn_SavesWhatWasTyped_ThenShowsTheDraftItWrote()
    {
        _handler.DraftJson = $$"""{"markdown":"The spa is quiet.","updatedAt":"{{StubHandler.DraftStamp:O}}","revisionCount":0}""";
        _handler.NoneWritten = true;
        JSInterop.Setup<string>("nornisEditor.getHtml", _ => true).SetResult("<p>The spa is quiet tonight.</p>");
        _handler.SendWritesDraft = true;
        _handler.ConversationAfterSendJson = WroteAnswerJson;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-design"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-talk-input textarea").Input("Make it louder."));
        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-talk-send").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Find("button.nornis-prep-design-undo").TextContent, Does.Contain("Undo (1)")));
        cut.WaitForAssertion(() => Assert.That(
            JSInterop.Invocations["nornisEditor.init"].Select(i => i.Arguments[1] as string),
            Has.Some.Contains("The spa hums with slime."), "the editor shows the draft the turn wrote"));
        var remount = JSInterop.Invocations["nornisEditor.init"].Single(i => (i.Arguments[1] as string)!.Contains("hums"));
        Assert.That(remount.Arguments[5], Is.False, "the copy kept from before the turn is never put back over what it wrote");
        var saveAt = _handler.Requests.FindIndex(r => r.StartsWith("PUT", StringComparison.Ordinal) && r.EndsWith("/draft", StringComparison.Ordinal));
        var sendAt = _handler.Requests.FindIndex(r => r.EndsWith("/conversation/messages", StringComparison.Ordinal));
        Assert.Multiple(() =>
        {
            Assert.That(saveAt, Is.GreaterThanOrEqualTo(0).And.LessThan(sendAt), "what was typed is saved before the turn, so its edits land on it");
            Assert.That(JsonDocument.Parse(_handler.LastDraftSaveBody!).RootElement.GetProperty("markdown").GetString(),
                Is.EqualTo("The spa is quiet tonight."));
            Assert.That(cut.FindAll(".nornis-prep-talk-wrote-summary"), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task OnTheSectionsTab_AnEditCantBeApplied()
    {
        _handler.ConversationJson = EditAnswerJson;
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-edit"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("button.nornis-prep-edit-apply").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find(".nornis-prep-edit").TextContent, Does.Contain("Open the Design tab to apply it."));
        });
    }

    [Test]
    public async Task Split_ShowsThePlan_SendsItAfterAMerge_AndOpensTheNewSections()
    {
        var cut = await RenderOnDesignAsync();
        var dialogs = Render<MudDialogProvider>();

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-design-split").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.FindAll(".nornis-split-row"), Has.Count.EqualTo(3)));
        Assert.That(dialogs.FindAll(".nornis-split-mode"), Is.Empty, "with nothing written, the split replaces the empty outline without asking");

        await dialogs.InvokeAsync(() => dialogs.FindAll("button.nornis-split-merge")[2].Click());
        await dialogs.InvokeAsync(() => dialogs.Find("button.nornis-split-apply").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastSplitBody, Is.Not.Null));
        var split = JsonDocument.Parse(_handler.LastSplitBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(split.GetProperty("plan").EnumerateArray().Select(p => p.GetProperty("startLine").GetInt32()), Is.EqualTo([1, 5]));
            Assert.That(split.GetProperty("mode").GetString(), Is.EqualTo("Replace"));
            Assert.That(split.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(), Is.EqualTo(StubHandler.DraftStamp));
        });
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-draft"), Has.Count.EqualTo(1), "the Sections tab opens"));
        Assert.That(cut.FindAll("button.nornis-prep-outline-title").Select(b => b.TextContent.Trim()), Is.EqualTo(["Arrival", "The Baths"]));
    }

    [Test]
    public async Task Gather_AsksFirstWhenThereIsADraft_AndSendsTheDraftsStamp()
    {
        JSInterop.Setup<string>("nornisEditor.getHtml", _ => true).SetResult("<p>Old.</p>");
        _handler.DraftJson = $$"""{"markdown":"Old.","updatedAt":"{{StubHandler.DraftStamp:O}}","revisionCount":0}""";
        var cut = await RenderAsync();
        var dialogs = Render<MudDialogProvider>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-mode button"), Has.Count.EqualTo(2)));
        await cut.InvokeAsync(() => ModeButton(cut, "Design").Click());
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-prep-design-gather"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-design-gather").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Gather the sections into the draft?")));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Gather").Click());

        cut.WaitForAssertion(() => Assert.That(_handler.LastGatherBody, Is.Not.Null));
        Assert.That(JsonDocument.Parse(_handler.LastGatherBody!).RootElement.GetProperty("basedOnUpdatedAt").GetDateTimeOffset(),
            Is.EqualTo(StubHandler.DraftStamp));
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

        /// <summary>The block warnings a save answers with, as a JSON array.</summary>
        public string SaveWarningsJson { get; set; } = "[]";
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

        /// <summary>What GET library answers: the world's documents, for the Documents card (feature 36).</summary>
        public string LibraryJson { get; set; } = "[]";

        public Guid AdventureId => _adventureId;

        // Design mode (feature 37).
        public static readonly DateTimeOffset DraftStamp = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

        /// <summary>The Adventure's draft as JSON; none until a test says otherwise.</summary>
        public string DraftJson { get; set; } = "null";

        /// <summary>The table brief as a JSON value.</summary>
        public string TableBriefJson { get; set; } = "null";

        /// <summary>The chosen source books as a JSON array.</summary>
        public string SourceBooksJson { get; set; } = "[]";

        /// <summary>Every section unwritten, so the page opens on Design.</summary>
        public bool NoneWritten { get; set; }

        public HttpStatusCode DraftSaveStatus { get; set; } = HttpStatusCode.OK;
        public string? LastDraftSaveBody { get; private set; }
        public string? LastBriefBody { get; private set; }
        public string? LastSourceBooksBody { get; private set; }
        public bool DraftUndone { get; private set; }
        public string? LastApplyBody { get; private set; }
        public string? LastSplitBody { get; private set; }
        public string? LastGatherBody { get; private set; }

        /// <summary>The sections a split answers with: a title each, all Scenes.</summary>
        public string[] SplitTitles { get; set; } = ["Arrival", "The Baths"];

        public bool SendFails { get; set; }

        /// <summary>The send stream is a Design turn that wrote into the draft (feature 41).</summary>
        public bool SendWritesDraft { get; set; }
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
                         "sortOrder":1,"updatedAt":"2026-11-02T11:00:00+00:00","isFilled":true,
                         "blockWarnings":{{SaveWarningsJson}}}
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
                var outcomes = SendWritesDraft ? ",\"draftEditOutcomes\":[\"Applied\"]" : "";
                var end = SendFails
                    ? "event: error\ndata: {\"code\":\"answer_cut_off\",\"message\":\"The answer ran past the length one answer can be and was cut off, so it wasn't saved.\"}\n\n"
                    : $"event: done\ndata: {{\"id\":\"{AnswerId}\",\"role\":\"Assistant\",\"content\":\"Three cultists.\",\"model\":\"nornis-ask\",\"createdAt\":\"2026-11-02T10:30:05+00:00\"{outcomes}}}\n\n";
                var stream = "event: delta\ndata: {\"text\":\"Three \"}\n\n" + (SendWritesDraft ? "event: writing\ndata: {}\n\n" : "") + end;
                if (SendWritesDraft)
                {
                    DraftJson = """{"markdown":"The spa hums with slime.","updatedAt":"2026-11-03T09:20:00+00:00","revisionCount":1}""";
                }

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

            if (request.Method == HttpMethod.Put && path.EndsWith("/draft", StringComparison.Ordinal))
            {
                LastDraftSaveBody = await request.Content!.ReadAsStringAsync(ct);
                if (DraftSaveStatus == HttpStatusCode.Conflict)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = new StringContent("""{"code":"draft_changed","message":"The draft changed since you opened it."}""",
                            Encoding.UTF8, "application/json"),
                    };
                }

                var markdown = JsonDocument.Parse(LastDraftSaveBody).RootElement.GetProperty("markdown").GetString();
                DraftJson = JsonSerializer.Serialize(new { markdown, updatedAt = "2026-11-03T09:05:00+00:00", revisionCount = 0 });
                return Json(DraftJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/draft/edits", StringComparison.Ordinal))
            {
                LastApplyBody = await request.Content!.ReadAsStringAsync(ct);
                DraftJson = """{"markdown":"The spa hums with slime.","updatedAt":"2026-11-03T09:20:00+00:00","revisionCount":1}""";
                return Json(DraftJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/draft/gather", StringComparison.Ordinal))
            {
                LastGatherBody = await request.Content!.ReadAsStringAsync(ct);
                DraftJson = """{"markdown":"## Overview\n\nCaptain Voss hides the key.","updatedAt":"2026-11-03T09:30:00+00:00","revisionCount":1}""";
                return Json(DraftJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/draft/plan", StringComparison.Ordinal))
            {
                return Json($$"""
                    {"rows":[{"startLine":1,"title":"Arrival","kind":"Scene","opening":"Steam everywhere."},
                             {"startLine":5,"title":"The Baths","kind":"Scene","opening":"Slimes lurk."},
                             {"startLine":9,"title":"The Boiler","kind":"Scene","opening":"It hisses."}],
                     "source":"Headings","note":null,"draftUpdatedAt":"{{DraftStamp:O}}"}
                    """);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/draft/split", StringComparison.Ordinal))
            {
                LastSplitBody = await request.Content!.ReadAsStringAsync(ct);
                return Json(SplitAdventure());
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/draft/undo", StringComparison.Ordinal))
            {
                DraftUndone = true;
                DraftJson = """{"markdown":"Before the Loremaster.","updatedAt":"2026-11-03T09:10:00+00:00","revisionCount":0}""";
                return Json(DraftJson);
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/brief", StringComparison.Ordinal))
            {
                LastBriefBody = await request.Content!.ReadAsStringAsync(ct);
                TableBriefJson = JsonSerializer.Serialize(JsonDocument.Parse(LastBriefBody).RootElement.GetProperty("brief").GetString());
                return Json(Adventure(OverviewId, SceneId));
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/source-books", StringComparison.Ordinal))
            {
                LastSourceBooksBody = await request.Content!.ReadAsStringAsync(ct);
                var chosen = JsonDocument.Parse(LastSourceBooksBody).RootElement.GetProperty("documentIds").EnumerateArray()
                    .Select(e => new { id = e.GetGuid(), title = "Chosen book", slug = (string?)null });
                SourceBooksJson = JsonSerializer.Serialize(chosen);
                return Json(Adventure(OverviewId, SceneId));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/prep/", StringComparison.Ordinal))
            {
                return Json(Adventure(OverviewId, SceneId));
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/library", StringComparison.Ordinal))
            {
                return Json(LibraryJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string Adventure(Guid first, Guid second)
        {
            string Section(Guid id, int order) => id == OverviewId
                ? $$"""{"id":"{{id}}","kind":"Overview","title":"Overview","bodyMarkdown":"Captain Voss hides the key.","sortOrder":{{order}},"updatedAt":"{{OverviewStamp:O}}","revisionCount":0,"isFilled":{{(NoneWritten ? "false" : "true")}}}"""
                : $$"""{"id":"{{id}}","kind":"Scene","title":"Scene 1: The Docks","bodyMarkdown":"","sortOrder":{{order}},"updatedAt":"2026-11-02T10:00:00+00:00","revisionCount":0,"isFilled":false}""";

            return $$"""
                {"id":"{{_adventureId}}","worldId":"{{_worldId}}","campaignId":null,"title":"The Silver Key",
                 "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                 "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-02T10:00:00+00:00",
                 "sections":[{{Section(first, 1)}},{{Section(second, 2)}}{{Chapter()}}],
                 "details":{"gameSystem":null,"edition":null,"levels":null,"year":null},
                 "printDetails":{"title":"The Silver Key","gameSystem":"Fate Accelerated","edition":null,
                   "type":"Nornis adventure","levels":null,"publisher":"Nornis Press","author":"kelda","year":null},
                 "draft":{{DraftJson}},"tableBrief":{{TableBriefJson}},"sourceBooks":{{SourceBooksJson}} }
                """;
        }

        private string SplitAdventure()
        {
            var sections = string.Join(",", SplitTitles.Select((t, i) =>
                $$"""{"id":"{{Guid.NewGuid()}}","kind":"Scene","title":"{{t}}","bodyMarkdown":"Text {{i}}.","sortOrder":{{i + 1}},"updatedAt":"2026-11-03T09:40:00+00:00","revisionCount":0,"isFilled":true}"""));
            return $$"""
                {"id":"{{_adventureId}}","worldId":"{{_worldId}}","campaignId":null,"title":"The Silver Key",
                 "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                 "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-03T09:40:00+00:00",
                 "sections":[{{sections}}],
                 "draft":{{DraftJson}},"tableBrief":null,"sourceBooks":[] }
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
