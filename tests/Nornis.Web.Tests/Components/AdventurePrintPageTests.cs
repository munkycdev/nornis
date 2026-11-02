using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
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
/// The booklet page and the Library's handling of a print (feature 29, Requirement 7): who gets
/// the page, that it shows the API's booklet as is, that File to Library files the PDF against
/// the Adventure, and that sharing a print asks first because it may hold the Hidden truth.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AdventurePrintPageTests : BunitContext
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
        JSInterop.Setup<bool>("nornisUpload.send", _ => true).SetResult(true);
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<AdventurePrint>> RenderPrintAsync()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        return Render<AdventurePrint>(p => p.Add(x => x.Key, "the-silver-key"));
    }

    [Test]
    public async Task APlayer_SeesGmsOnly_AndThePageNeverAsksForTheBooklet()
    {
        _handler.Role = "Player";

        var cut = await RenderPrintAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("GMs only")));
        Assert.That(_handler.Requests, Has.None.Contains("/prep"));
    }

    [Test]
    public async Task TheGm_SeesTheBookletAsTheApiRenderedIt_WithPrintAndFileToLibrary()
    {
        var cut = await RenderPrintAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-booklet-page .booklet"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-booklet-page blockquote.read-aloud").TextContent, Does.Contain("The vault door is cold."));
            Assert.That(cut.FindAll("button.nornis-booklet-print"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll("button.nornis-booklet-file"), Has.Count.EqualTo(1));
            Assert.That(_handler.Requests, Has.Some.EndsWith($"/prep/{StubHandler.AdventureId}/print"),
                "the page resolves the slug, then asks for the booklet by id");
        });
    }

    [Test]
    public async Task Print_CallsTheBrowsersPrint()
    {
        var cut = await RenderPrintAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-booklet-print"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-booklet-print").Click());

        Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "print"), Is.True);
    }

    [Test]
    public async Task FileToLibrary_UploadsThePdf_AgainstTheAdventure_AsAGmOnlyHandout()
    {
        var cut = await RenderPrintAsync();
        var dialogs = Render<MudDialogProvider>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-booklet-file"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-booklet-file").Click());
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain("Save as PDF")));
        Assert.Multiple(() =>
        {
            Assert.That(dialogs.Markup, Does.Contain("The Silver Key, printed 2026-11-02"));
            Assert.That(dialogs.Markup, Does.Not.Contain("Visibility"), "a print is always filed GM only");
            Assert.That(dialogs.Find("input[type=file]").GetAttribute("accept"), Is.EqualTo(".pdf"));
        });

        dialogs.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("%PDF-1.7", "booklet.pdf", contentType: "application/pdf"));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Upload").Click());

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Filed as")));
        var body = JsonDocument.Parse(_handler.LastUploadBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(body.GetProperty("adventureId").GetGuid(), Is.EqualTo(StubHandler.AdventureId));
            Assert.That(body.GetProperty("kind").GetString(), Is.EqualTo("Handout"));
            Assert.That(body.GetProperty("visibility").GetString(), Is.EqualTo("GMOnly"));
            Assert.That(_handler.Requests, Has.Some.EndsWith($"/library/{StubHandler.DocumentId}/confirm"));
            Assert.That(cut.Find(".nornis-booklet-filed a").GetAttribute("href"), Is.EqualTo("/library/the-silver-key-printed-2026-11-02"));
        });
    }

    [Test]
    public async Task SharingAPrint_AsksFirst_AndKeepingItGmOnlySendsNothing()
    {
        var (chip, dialogs) = await RenderChipAsync(adventureId: StubHandler.AdventureId);

        await ChooseAsync(chip, "Party visible");

        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain(LibraryVisibilityChip.SharePrintWarning)));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Keep it GM only").Click());

        Assert.That(_handler.Requests, Has.None.EndsWith("/visibility"));
    }

    [Test]
    public async Task SharingAPrint_OnceConfirmed_MakesItPartyVisible()
    {
        var (chip, dialogs) = await RenderChipAsync(adventureId: StubHandler.AdventureId);

        await ChooseAsync(chip, "Party visible");
        dialogs.WaitForAssertion(() => Assert.That(dialogs.Markup, Does.Contain(LibraryVisibilityChip.SharePrintWarning)));
        await dialogs.InvokeAsync(() => dialogs.FindAll("button").First(b => b.TextContent.Trim() == "Share it").Click());

        chip.WaitForAssertion(() => Assert.That(_handler.Requests, Has.One.EndsWith("/visibility")));
    }

    [Test]
    public async Task SharingAnyOtherDocument_AsksNothing()
    {
        var (chip, dialogs) = await RenderChipAsync(adventureId: null);

        await ChooseAsync(chip, "Party visible");

        chip.WaitForAssertion(() => Assert.That(_handler.Requests, Has.One.EndsWith("/visibility")));
        Assert.That(dialogs.Markup, Does.Not.Contain(LibraryVisibilityChip.SharePrintWarning));
    }

    private IRenderedComponent<MudPopoverProvider>? _popovers;

    private async Task<(IRenderedComponent<LibraryVisibilityChip> Chip, IRenderedComponent<MudDialogProvider> Dialogs)> RenderChipAsync(
        Guid? adventureId)
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        _popovers = Render<MudPopoverProvider>();
        var dialogs = Render<MudDialogProvider>();
        var document = StubHandler.Document("GMOnly", adventureId);
        var chip = Render<LibraryVisibilityChip>(p => p.Add(x => x.Document, document));
        return (chip, dialogs);
    }

    private async Task ChooseAsync(IRenderedComponent<LibraryVisibilityChip> chip, string item)
    {
        await chip.InvokeAsync(() => chip.Find(".mud-chip").Click());
        var popovers = _popovers!;
        popovers.WaitForAssertion(() => Assert.That(popovers.FindAll(".mud-menu-item").Any(m => m.TextContent.Trim() == item)));
        await popovers.InvokeAsync(() => popovers.FindAll(".mud-menu-item").First(m => m.TextContent.Trim() == item).Click());
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public static readonly Guid AdventureId = Guid.NewGuid();
        public static readonly Guid DocumentId = Guid.NewGuid();
        private static readonly Guid WorldId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public List<string> Requests { get; } = [];
        public string? LastUploadBody { get; private set; }

        public static LibraryDocumentDto Document(string visibility, Guid? adventureId) => new(
            DocumentId, WorldId, "The Silver Key, printed 2026-11-02", "booklet.pdf", "application/pdf", 8, "Handout",
            visibility, "Indexing", null, 0, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "the-silver-key-printed-2026-11-02", adventureId);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add($"{request.Method} {path}");

            if (path == "/api/worlds")
            {
                return Json($$"""
                    [{"id":"{{WorldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"{{Role}}"}]
                    """);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/print", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(new AdventurePrintDto(AdventureId, "The Silver Key", "the-silver-key",
                    "<article class=\"booklet\"><section class=\"booklet-section\"><blockquote class=\"read-aloud\"><p>The vault door is cold.</p></blockquote></section></article>",
                    "The Silver Key, printed 2026-11-02"), JsonSerializerOptions.Web));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/prep/", StringComparison.Ordinal))
            {
                return Json($$"""
                    {"id":"{{AdventureId}}","worldId":"{{WorldId}}","campaignId":null,"title":"The Silver Key",
                     "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                     "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-02T10:00:00+00:00","sections":[]}
                    """);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/library/request-upload", StringComparison.Ordinal))
            {
                LastUploadBody = await request.Content!.ReadAsStringAsync(ct);
                var ticket = new LibraryUploadTicketDto(Document("GMOnly", AdventureId), "https://blob.example/upload");
                return Json(JsonSerializer.Serialize(ticket, JsonSerializerOptions.Web));
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/confirm", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(Document("GMOnly", AdventureId), JsonSerializerOptions.Web));
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/visibility", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(Document("PartyVisible", AdventureId), JsonSerializerOptions.Web));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
