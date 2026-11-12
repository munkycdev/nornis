using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Navigation;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;
using SourcesPage = Nornis.Web.Components.Pages.Sources;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The sources list's Type row (3.38): it opens on the sessions, the choice lives in the
/// address, and a way back from a source that is not a session asks for every type.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class SourcesPageTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var auth = new AuthSessionState();
        var api = new NornisApiClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost") }, viewAs, signal, auth);

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(auth);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    private async Task<IRenderedComponent<SourcesPage>> RenderAsync(string url)
    {
        Nav.NavigateTo(url);
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        var cut = Render<SourcesPage>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll("[aria-label='Filter by type']"), Has.Count.EqualTo(1)));
        return cut;
    }

    private static IReadOnlyList<string> Titles(IRenderedComponent<SourcesPage> cut) =>
        cut.FindAll(".nornis-list-title").Select(e => e.TextContent.Trim()).ToList();

    private static AngleSharp.Dom.IElement TypeChip(IRenderedComponent<SourcesPage> cut, string label) =>
        cut.FindAll("[aria-label='Filter by type'] .mud-chip").Single(c => c.TextContent.Trim() == label);

    [Test]
    public async Task ThePlainList_ShowsOnlyTheSessions_HoweverTheyWereCaptured()
    {
        var cut = await RenderAsync("/sources");

        Assert.That(Titles(cut), Is.EquivalentTo(["Session 12", "Old notebook", "Backlog session"]));
    }

    [Test]
    public async Task TheTypeRow_OffersOnlyTheTypesTheWorldHas_InDisplayOrder()
    {
        var cut = await RenderAsync("/sources");

        var chips = cut.FindAll("[aria-label='Filter by type'] .mud-chip").Select(c => c.TextContent.Trim());
        Assert.That(chips, Is.EqualTo(["All", "Session note", "Handwritten note", "Imported note", "GM note", "Map"]));
    }

    [Test]
    public async Task All_ShowsEveryType_AndSaysSoInTheAddress()
    {
        var cut = await RenderAsync("/sources");

        TypeChip(cut, "All").Click();

        cut.WaitForAssertion(() => Assert.That(Titles(cut), Has.Count.EqualTo(5)));
        Assert.That(Nav.Uri, Does.EndWith("/sources?type=all"));
    }

    [Test]
    public async Task PickingATypeFromAll_NarrowsToIt_AndTheAddressNamesIt()
    {
        var cut = await RenderAsync("/sources?type=all");

        TypeChip(cut, "Map").Click();

        cut.WaitForAssertion(() => Assert.That(Titles(cut), Is.EqualTo(["The vale"])));
        Assert.That(Nav.Uri, Does.EndWith("/sources?type=Map"));
    }

    [Test]
    public async Task TurningOffTheLastLitType_ShowsEverything_RatherThanNothing()
    {
        var cut = await RenderAsync("/sources?type=Map");

        TypeChip(cut, "Map").Click();

        cut.WaitForAssertion(() => Assert.That(Titles(cut), Has.Count.EqualTo(5)));
        Assert.That(Nav.Uri, Does.EndWith("/sources?type=all"));
    }

    [Test]
    public async Task TheAddress_RestoresAChoice_AndReturningToTheDefaultDropsIt()
    {
        var cut = await RenderAsync("/sources?type=gmnote,Map");
        Assert.That(Titles(cut), Is.EquivalentTo(["The vale", "Who the miller is"]), "case-insensitive, as typed links are");

        Nav.NavigateTo("/sources");

        // The sidebar link lands on this same page instance and must bring the default back.
        cut.WaitForAssertion(() => Assert.That(Titles(cut), Has.Count.EqualTo(3)));
    }

    [Test]
    public async Task AnAddressNamingNoKnownType_KeepsTheDefault()
    {
        var cut = await RenderAsync("/sources?type=Scroll");

        Assert.That(Titles(cut), Has.Count.EqualTo(3));
    }

    [Test]
    public async Task NothingMatching_SaysWhichFiltersAreOn()
    {
        var cut = await RenderAsync("/sources?type=Map");

        cut.FindAll("[aria-label='Filter by status'] .mud-chip").Single(c => c.TextContent.Trim() == "Failed").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("No sources of the chosen types are failed.")));
    }

    [TestCase("SessionNote", "/sources")]
    [TestCase("ImportedNote", "/sources")]
    [TestCase("SessionAudio", "/sources")]
    [TestCase("Map", "/sources?type=all")]
    [TestCase("GMNote", "/sources?type=all")]
    [TestCase("AdventureExport", "/sources?type=all")]
    public void TheWayBackFromASource_ShowsIt(string type, string expected) =>
        Assert.That(Links.SourcesShowing(type), Is.EqualTo(expected));

    [Test]
    public void EveryTypeTheDomainHas_HasAPlaceInTheTypeRow() =>
        // Web does not reference Domain, so the names are listed here; a new SourceType that is
        // not added to FilterOrder still gets a chip (after the known ones), but unlabelled.
        Assert.That(SourceTypeDisplay.FilterOrder, Is.EquivalentTo(
        [
            "SessionNote", "JournalEntry", "Transcript", "Upload", "Image", "HandwrittenNotes", "WebLink", "GMNote",
            "ImportedNote", "SessionAudio", "FanFiction", "Map", "Reveal", "LibraryExcerpt", "AdventureExport",
        ]));

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/api/worlds" => Json($$"""
                    [{"id":"{{WorldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"GM"}]
                    """),
                _ when path.EndsWith("/sources", StringComparison.Ordinal) => Json($"[{Source("SessionNote", "Session 12")},{Source("HandwrittenNotes", "Old notebook")},{Source("ImportedNote", "Backlog session")},{Source("Map", "The vale")},{Source("GMNote", "Who the miller is")}]"),
                _ when path.EndsWith("/campaigns", StringComparison.Ordinal) => Json("[]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static string Source(string type, string title) => $$"""
            {"id":"{{Guid.NewGuid()}}","worldId":"{{WorldId}}","type":"{{type}}","title":"{{title}}","occurredAt":null,
             "createdAt":"2026-11-01T00:00:00Z","createdByUserId":"{{Guid.NewGuid()}}","visibility":"Party","processingStatus":"Processed"}
            """;

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
