using System.Net;
using System.Text;
using System.Text.Json;
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
/// The booklet page and the Library's handling of a print (features 29 and 34): who gets the page,
/// that it shows the API's PDF in the browser's viewer, that File to Library files it in one click,
/// and that sharing a print asks first because it may hold the Hidden truth.
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
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Requests, Has.None.Contains("/prep"));
            Assert.That(cut.FindAll("iframe"), Is.Empty);
        });
    }

    [Test]
    public async Task TheGm_SeesTheApisPdf_InTheBrowsersViewer_WithDownloadAndFileToLibrary()
    {
        var cut = await RenderPrintAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("iframe.nornis-booklet-frame"), Has.Count.EqualTo(1)));
        var booklet = $"/prep-booklets/{StubHandler.WorldId}/{StubHandler.AdventureId}";
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find("iframe.nornis-booklet-frame").GetAttribute("src"), Is.EqualTo(booklet),
                "the page resolves the slug, then shows the booklet by id through the Web's own route");
            Assert.That(cut.Find(".nornis-booklet-open a").GetAttribute("href"), Is.EqualTo(booklet));
            Assert.That(cut.Find("a.nornis-booklet-download").GetAttribute("href"), Is.EqualTo(booklet + "?download=true"));
            Assert.That(cut.FindAll("button.nornis-booklet-file"), Has.Count.EqualTo(1));
            Assert.That(_handler.Requests, Has.None.Contains("/booklet"), "the browser fetches the PDF, not the circuit");
        });
    }

    [Test]
    public async Task ThePagesTitle_IsTheAdventuresTitleAlone()
    {
        var cut = await RenderPrintAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("iframe.nornis-booklet-frame"), Has.Count.EqualTo(1)));

        var title = Render(cut.FindComponent<PageTitle>().Instance.ChildContent!);

        Assert.That(title.Markup.Trim(), Is.EqualTo("The Silver Key"));
    }

    [Test]
    public async Task FileToLibrary_FilesTheBookletInOneClick_AndLinksTheDocument()
    {
        var cut = await RenderPrintAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-booklet-file"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-booklet-file").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-booklet-filed"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Requests, Has.One.EqualTo($"POST /api/worlds/{StubHandler.WorldId}/prep/{StubHandler.AdventureId}/booklet/file"));
            Assert.That(_handler.Requests, Has.None.Contains("/library/request-upload"), "nothing is uploaded from the browser");
            Assert.That(cut.Find(".nornis-booklet-filed a").GetAttribute("href"), Is.EqualTo("/library/the-silver-key-printed-2026-11-02"));
            Assert.That(cut.Find(".nornis-booklet-filed").TextContent, Does.Contain("GM only"));
            Assert.That(cut.FindAll(".nornis-booklet-file-error"), Is.Empty);
        });
    }

    [Test]
    public async Task FileToLibrary_WhenTheApiRefuses_ShowsWhy_AndFilesNothing()
    {
        _handler.FileFails = true;
        var cut = await RenderPrintAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("button.nornis-booklet-file"), Has.Count.EqualTo(1)));

        await cut.InvokeAsync(() => cut.Find("button.nornis-booklet-file").Click());

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-booklet-file-error"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-booklet-file-error").TextContent, Does.Contain("The booklet couldn't be laid out."));
            Assert.That(cut.FindAll(".nornis-booklet-filed"), Is.Empty);
            Assert.That(cut.Find("button.nornis-booklet-file").HasAttribute("disabled"), Is.False, "the GM can try again");
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
        public static readonly Guid WorldId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public bool FileFails { get; set; }
        public List<string> Requests { get; } = [];

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

            if (request.Method == HttpMethod.Post && path.EndsWith("/booklet/file", StringComparison.Ordinal))
            {
                return FileFails
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("""{"code":"print_failed","message":"The booklet couldn't be laid out."}""",
                            Encoding.UTF8, "application/json"),
                    }
                    : Json(JsonSerializer.Serialize(Document("GMOnly", AdventureId), JsonSerializerOptions.Web));
            }

            if (request.Method == HttpMethod.Get && path.Contains("/prep/", StringComparison.Ordinal))
            {
                return Json($$"""
                    {"id":"{{AdventureId}}","worldId":"{{WorldId}}","campaignId":null,"title":"The Silver Key",
                     "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                     "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-02T10:00:00+00:00","sections":[]}
                    """);
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
