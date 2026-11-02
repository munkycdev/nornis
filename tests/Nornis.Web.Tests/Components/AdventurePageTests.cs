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

    private sealed class StubHandler : HttpMessageHandler
    {
        public static readonly Guid OverviewId = Guid.NewGuid();
        public static readonly Guid SceneId = Guid.NewGuid();
        public static readonly DateTimeOffset OverviewStamp = new(2026, 11, 2, 10, 0, 0, TimeSpan.Zero);

        private readonly Guid _worldId = Guid.NewGuid();
        private readonly Guid _adventureId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public HttpStatusCode SaveStatus { get; set; } = HttpStatusCode.OK;
        public List<string> Requests { get; } = [];
        public string? LastSaveBody { get; private set; }
        public string? LastReorderBody { get; private set; }

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
                 "sections":[{{Section(first, 1)}},{{Section(second, 2)}}]}
                """;
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
