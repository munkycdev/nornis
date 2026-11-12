using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.State;
using NUnit.Framework;
using PrepPage = Nornis.Web.Components.Pages.Prep;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Prep list: its spend line (feature 29), for the GM only, and its shelf of books (feature 42),
/// which filters by whether each was exported or published.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PrepPageTests : BunitContext
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

    private async Task<IRenderedComponent<PrepPage>> RenderAsync()
    {
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        return Render<PrepPage>();
    }

    [Test]
    public async Task TheGm_SeesTheMonthsDraftingSpend_AgainstItsCeiling()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Drafting this month: $1.25 of $10.00")));
    }

    [Test]
    public async Task ASpentCeiling_SaysSo()
    {
        _handler.Budget = """{"spentThisMonthUsd":10.02,"monthlyBudgetUsd":10.0,"isExceeded":true}""";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("$10.02 of $10.00, spent until next month")));
    }

    [Test]
    public async Task APlayer_NeverAsksForTheSpend()
    {
        _handler.Role = "Player";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("GMs only")));
        Assert.That(_handler.Requests, Has.None.Contains("prep-budget"));
    }

    [Test]
    public async Task TheAdventures_AreShelvedAsBooks()
    {
        _handler.Adventures = AllFour;

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-book"), Has.Count.EqualTo(4)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-shelf .nornis-shelf-book"), Has.Count.EqualTo(4));
            Assert.That(cut.FindAll(".nornis-prep-exported"), Has.Count.EqualTo(2));
            Assert.That(cut.FindAll(".nornis-prep-published"), Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// Requirement 5.1: the cover art through the Cover card's own proxy, stamped so a new cover is
    /// a new URL; an adventure with no art gets the typeset cover and asks for no image at all.
    /// </summary>
    [Test]
    public async Task ABookWithArt_ShowsItsCover_AndOneWithout_IsTypeset()
    {
        var stamp = DateTimeOffset.Parse("2026-11-07T20:00:00Z");
        _handler.Adventures = "[" + Row("Painted", exported: false, printed: false, coverUpdatedAt: stamp) + ","
            + Row("Plain", exported: false, printed: false) + "]";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-book"), Has.Count.EqualTo(2)));
        var painted = cut.FindAll(".nornis-prep-book")[0];
        var plain = cut.FindAll(".nornis-prep-book")[1];
        Assert.Multiple(() =>
        {
            Assert.That(painted.QuerySelector(".nornis-shelf-cover-art img")!.GetAttribute("src"),
                Does.StartWith("/prep-covers/").And.EndWith($"?v={stamp.ToUnixTimeMilliseconds()}"));
            Assert.That(plain.QuerySelector("img"), Is.Null);
            Assert.That(plain.QuerySelector(".nornis-shelf-cover-typeset")!.TextContent, Does.Contain("Plain"));
        });
    }

    [TestCase("In the record", "Exported only,Both")]
    [TestCase("In the Library", "Published only,Both")]
    [TestCase("Neither", "Untouched")]
    [TestCase("All", "Untouched,Exported only,Published only,Both")]
    public async Task AFilterChip_ShowsOnlyTheAdventuresItNames(string chip, string expected)
    {
        _handler.Adventures = AllFour;
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-book"), Has.Count.EqualTo(4)));

        cut.FindAll(".nornis-prep-filters .mud-chip").Single(c => c.TextContent.Trim() == chip).Click();

        cut.WaitForAssertion(() => Assert.That(
            string.Join(",", cut.FindAll(".nornis-prep-book .nornis-prep-book-title").Select(t => t.TextContent.Trim())),
            Is.EqualTo(expected)));
    }

    [Test]
    public async Task AFilterThatMatchesNothing_SaysSo()
    {
        _handler.Adventures = $"[{Row("Untouched", exported: false, printed: false)}]";
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-prep-book"), Has.Count.EqualTo(1)));

        cut.FindAll(".nornis-prep-filters .mud-chip").Single(c => c.TextContent.Trim() == "In the record").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("No adventures have been added to the record.")));
        Assert.That(cut.FindAll(".nornis-prep-book"), Is.Empty);
    }

    private static readonly string AllFour = "[" + string.Join(",",
        Row("Untouched", exported: false, printed: false),
        Row("Exported only", exported: true, printed: false),
        Row("Published only", exported: false, printed: true),
        Row("Both", exported: true, printed: true)) + "]";

    private static string Row(string title, bool exported, bool printed, DateTimeOffset? coverUpdatedAt = null)
    {
        var print = printed ? $"\"{Guid.NewGuid()}\"" : "null";
        var cover = coverUpdatedAt is { } stamp ? $"\"{stamp:O}\"" : "null";
        return $$"""
            {"id":"{{Guid.NewGuid()}}","title":"{{title}}","slug":null,"campaignId":null,"sectionCount":3,
             "filledSectionCount":1,"lastPrintedDocumentId":{{print}},"updatedAt":"2026-11-03T00:00:00Z",
             "isExported":{{(exported ? "true" : "false")}},"coverUpdatedAt":{{cover}}}
            """;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Guid _worldId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public string Budget { get; set; } = """{"spentThisMonthUsd":1.25,"monthlyBudgetUsd":10.0,"isExceeded":false}""";
        public string Adventures { get; set; } = "[]";
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            return Task.FromResult(path switch
            {
                "/api/worlds" => Json($$"""
                    [{"id":"{{_worldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"{{Role}}"}]
                    """),
                _ when path.EndsWith("/prep-budget", StringComparison.Ordinal) => Json(Budget),
                _ when path.EndsWith("/prep", StringComparison.Ordinal) => Json(Adventures),
                _ when path.EndsWith("/campaigns", StringComparison.Ordinal) => Json("[]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            });
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
