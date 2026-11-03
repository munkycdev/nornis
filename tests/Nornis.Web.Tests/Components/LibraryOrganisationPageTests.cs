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
/// Library organisation (feature 36) on the Library page and a document's page: the category
/// chips and their counts, the Shelf against one Adventure's documents, search and sort, the
/// remembered chip, and that only the GM sees where documents belong or can change it.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class LibraryOrganisationPageTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid SpaId = Guid.NewGuid();
    private static readonly Guid UnusedId = Guid.NewGuid();
    private static readonly Guid MapId = Guid.NewGuid();

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
        // The document page's rail brings the Loremaster, which isn't what these tests are about.
        ComponentFactories.AddStub<ContextRail>();
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<Library>> RenderLibraryAsync()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        var cut = Render<Library>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-list-row"), Is.Not.Empty));
        return cut;
    }

    private static IEnumerable<string> Titles(IRenderedComponent<Library> cut) =>
        cut.FindAll(".nornis-list-row .nornis-list-title").Select(t => t.TextContent.Trim());

    private static IEnumerable<string> Chips(IRenderedComponent<Library> cut) =>
        cut.FindAll(".nornis-library-filters .mud-chip").Select(c => c.TextContent.Trim());

    private static void ClickChip(IRenderedComponent<Library> cut, string label) =>
        cut.FindAll(".nornis-library-filters .mud-chip").Single(c => c.TextContent.Trim().StartsWith(label, StringComparison.Ordinal)).Click();

    [Test]
    public async Task TheShelf_HidesAnAdventuresUploads_AndTheChipsCountWhatIsOnIt()
    {
        var cut = await RenderLibraryAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Titles(cut), Is.EqualTo(["Harbor map", "The Slurping Spa", "Player's Guide"]), "newest first");
            // The Spa's handout is off the shelf, so there is no Handouts chip at all.
            Assert.That(Chips(cut), Is.EqualTo(["All · 3", "Books · 1", "Maps · 1", "Prints · 1"]));
        });
    }

    [Test]
    public async Task AChip_ShowsOnlyItsCategory()
    {
        var cut = await RenderLibraryAsync();

        ClickChip(cut, "Maps");

        cut.WaitForAssertion(() => Assert.That(Titles(cut), Is.EqualTo(["Harbor map"])));
    }

    [Test]
    public async Task ChoosingAnAdventure_ShowsItsUploadsAndPrints_AndTheChipsFollow()
    {
        var cut = await RenderLibraryAsync();
        var menu = cut.FindComponent<MudSelect<string>>();

        await cut.InvokeAsync(() => menu.Instance.ValueChanged.InvokeAsync(SpaId.ToString()));

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(Titles(cut), Is.EquivalentTo((string[])["Spa handout", "The Slurping Spa"]));
            Assert.That(Chips(cut), Is.EqualTo(["All · 2", "Handouts · 1", "Prints · 1"]));
        }));
    }

    [Test]
    public async Task ALinkedUpload_CarriesItsAdventuresName_ForTheGm()
    {
        var cut = await RenderLibraryAsync();
        var menu = cut.FindComponent<MudSelect<string>>();
        await cut.InvokeAsync(() => menu.Instance.ValueChanged.InvokeAsync("everything"));

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-library-adventure-tag").TextContent,
            Does.Contain("For The Slurping Spa")));
    }

    [Test]
    public async Task Search_MatchesTitlesIgnoringCase_AndSaysSoWhenNothingMatches()
    {
        var cut = await RenderLibraryAsync();
        var search = cut.FindComponent<MudTextField<string>>();

        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("HARBOR"));
        cut.WaitForAssertion(() => Assert.That(Titles(cut), Is.EqualTo(["Harbor map"])));

        await cut.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("dragon"));
        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-library-filter-empty").TextContent,
            Does.Contain("No documents here match that search.")));
    }

    [Test]
    public async Task SortByTitle_OrdersAlphabetically()
    {
        var cut = await RenderLibraryAsync();

        cut.FindAll(".nornis-library-sort .mud-chip").Single(c => c.TextContent.Trim() == "Title").Click();

        cut.WaitForAssertion(() => Assert.That(Titles(cut), Is.EqualTo(["Harbor map", "Player's Guide", "The Slurping Spa"])));
    }

    [Test]
    public async Task TheChosenChip_IsRememberedForThisBrowser()
    {
        JSInterop.Setup<string?>("localStorage.getItem", i => Equals(i.Arguments[0], "nornis.library.category")).SetResult("Book");

        var cut = await RenderLibraryAsync();
        Assert.That(Titles(cut), Is.EqualTo(["Player's Guide"]));

        ClickChip(cut, "Maps");

        var saved = JSInterop.Invocations.Where(i => i.Identifier == "localStorage.setItem").Last();
        Assert.That(saved.Arguments, Is.EqualTo(new object[] { "nornis.library.category", "Map" }));
    }

    [Test]
    public async Task APlayer_SeesNoAdventureMenuOrTags_AndNeverAsksForAdventures()
    {
        _handler.Role = "Player";

        var cut = await RenderLibraryAsync();

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-library-adventure-menu"), Is.Empty);
            Assert.That(cut.FindAll(".nornis-library-adventure-tag"), Is.Empty);
            Assert.That(_handler.Requests, Has.None.EndsWith("/prep"));
        });
    }

    [Test]
    public async Task TheGm_CanFileAnUploadUnderAnAdventure_FromItsPage()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        var cut = Render<LibraryDocumentDetail>(p => p.Add(x => x.Key, MapId.ToString()));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-library-organise"), Has.Count.EqualTo(1)));

        var belongs = cut.FindComponents<MudSelect<string>>().Single(s => s.Instance.Label == "Belongs to");
        await cut.InvokeAsync(() => belongs.Instance.ValueChanged.InvokeAsync(SpaId.ToString()));

        cut.WaitForAssertion(() => Assert.That(_handler.LastLinkBody, Does.Contain(SpaId.ToString())));
        Assert.That(cut.Find(".nornis-library-belongs-link").GetAttribute("href"), Does.Contain(SpaId.ToString()));
    }

    [Test]
    public async Task TheGm_CanChangeAnUploadsCategory_WhichSendsItsKind()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        var cut = Render<LibraryDocumentDetail>(p => p.Add(x => x.Key, MapId.ToString()));
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-library-organise"), Has.Count.EqualTo(1)));

        var category = cut.FindComponents<MudSelect<string>>().Single(s => s.Instance.Label == "Category");
        await cut.InvokeAsync(() => category.Instance.ValueChanged.InvokeAsync("Book"));

        cut.WaitForAssertion(() => Assert.That(_handler.LastKindBody, Does.Contain("\"Sourcebook\"")));
    }

    [TestCase("GM", "Player's Guide")]
    [TestCase("GM", "The Slurping Spa")]
    [TestCase("Player", "Harbor map")]
    public async Task NoOrganiseCard_ForAPlayer_ABookOrAPrint(string role, string title)
    {
        _handler.Role = role;
        var id = Documents().Single(d => d.Title == title).Id;
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();

        var cut = Render<LibraryDocumentDetail>(p => p.Add(x => x.Key, id.ToString()));

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain(title)));
        Assert.That(cut.FindAll(".nornis-library-organise"), Is.Empty);
    }

    private static LibraryDocumentDto[] Documents() =>
    [
        Doc(MapId, "Harbor map", "Map", "Map", minutesAgo: 1),
        Doc(Guid.Parse("3f1c7b2a-0000-4000-8000-000000000002"), "The Slurping Spa", "Map", "Print", minutesAgo: 2, print: SpaId),
        Doc(Guid.Parse("3f1c7b2a-0000-4000-8000-000000000003"), "Spa handout", "Handout", "Handout", minutesAgo: 3, linked: SpaId),
        Doc(Guid.Parse("3f1c7b2a-0000-4000-8000-000000000004"), "Player's Guide", "Sourcebook", "Book", minutesAgo: 4, origin: "Bibliotaph"),
    ];

    private static LibraryDocumentDto Doc(
        Guid id, string title, string kind, string category, int minutesAgo,
        Guid? linked = null, Guid? print = null, string origin = "Upload") => new(
        id, WorldId, title, "file.png", "image/png", 1024, kind, "PartyVisible", "Stored", null, 0, null,
        Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-minutesAgo), DateTimeOffset.UtcNow,
        Slug: null, AdventureId: print, Origin: origin, Category: category, LinkedAdventureId: linked,
        OnShelf: linked is null);

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Role { get; set; } = "GM";
        public List<string> Requests { get; } = [];
        public string? LastLinkBody { get; private set; }
        public string? LastKindBody { get; private set; }

        public AdventureSummaryDto[] Adventures { get; } =
        [
            new(SpaId, "The Slurping Spa", null, null, 3, 3, null, DateTimeOffset.UtcNow, false),
            new(UnusedId, "Nothing filed yet", null, null, 1, 0, null, DateTimeOffset.UtcNow, false),
        ];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);

            if (path == "/api/worlds")
            {
                return Json($$"""
                    [{"id":"{{WorldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"{{Role}}"}]
                    """);
            }

            if (path.EndsWith("/library/bibliotaph", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(new LibraryBibliotaphDto(LibraryBibliotaphDto.None, null), JsonSerializerOptions.Web));
            }

            if (path.EndsWith("/library", StringComparison.Ordinal))
            {
                // What a player gets: no links, and everything on the shelf.
                var documents = Role == "GM"
                    ? Documents()
                    : Documents().Select(d => d with { LinkedAdventureId = null, OnShelf = true }).ToArray();
                return Json(JsonSerializer.Serialize(documents, JsonSerializerOptions.Web));
            }

            if (path.EndsWith("/prep", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(Adventures, JsonSerializerOptions.Web));
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/adventure-link", StringComparison.Ordinal))
            {
                LastLinkBody = await request.Content!.ReadAsStringAsync(ct);
                return Json(JsonSerializer.Serialize(Documents()[0] with { LinkedAdventureId = SpaId, OnShelf = false }, JsonSerializerOptions.Web));
            }

            if (request.Method == HttpMethod.Put && path.EndsWith("/kind", StringComparison.Ordinal))
            {
                LastKindBody = await request.Content!.ReadAsStringAsync(ct);
                return Json(JsonSerializer.Serialize(Documents()[0] with { Kind = "Sourcebook", Category = "Book" }, JsonSerializerOptions.Web));
            }

            var document = Documents().FirstOrDefault(d => path.EndsWith($"/library/{d.Id}", StringComparison.Ordinal));
            if (document is not null)
            {
                return Json(JsonSerializer.Serialize(document, JsonSerializerOptions.Web));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
