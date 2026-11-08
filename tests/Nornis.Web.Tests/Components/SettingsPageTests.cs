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
using AdminPage = Nornis.Web.Components.Pages.Admin;
using MembersPage = Nornis.Web.Components.Pages.Members;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The world's Settings page (Nornis 3.23): five tabs with the shown one in the URL, one form
/// shared by every tab so typing survives a switch, each Save sending only its own fields; and
/// invite links on the Members page, for the GM only.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class SettingsPageTests : BunitContext
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

    private async Task<IRenderedComponent<AdminPage>> RenderAsync(string url = "/admin")
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        return Render<AdminPage>();
    }

    private static string ActiveTab(IRenderedComponent<AdminPage> cut) =>
        cut.Find(".nornis-settings-tabs .nornis-tab-active").GetAttribute("data-tab")!;

    [Test]
    public async Task ThePage_OpensOnTheWorldTab_WithTheKeyArtBesideTheName()
    {
        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-settings-tabs .nornis-tab"), Has.Count.EqualTo(5)));
        Assert.Multiple(() =>
        {
            Assert.That(ActiveTab(cut), Is.EqualTo("world"));
            Assert.That(cut.FindAll(".nornis-settings-world-grid .nornis-settings-world"), Has.Count.EqualTo(1));
            Assert.That(cut.FindAll(".nornis-settings-world-grid .nornis-keyart-card"), Has.Count.EqualTo(1));
            Assert.That(cut.Markup, Does.Not.Contain("Invite links"), "invites live on Members now");
            Assert.That(cut.Markup, Does.Not.Contain("Campaigns and members"), "the signpost card is gone");
        });
    }

    [TestCase("/admin?tab=ai", "ai", ".nornis-settings-budgets")]
    [TestCase("/admin?tab=Public", "public", ".nornis-settings-publicpage")]
    [TestCase("/admin?tab=processing", "processing", ".nornis-settings-review")]
    [TestCase("/admin?tab=export", "export", ".nornis-danger")]
    [TestCase("/admin?tab=nonsense", "world", ".nornis-settings-world")]
    public async Task TheTabInTheUrl_IsTheOneShown(string url, string tab, string selector)
    {
        var cut = await RenderAsync(url);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(selector), Has.Count.EqualTo(1)));
        Assert.That(ActiveTab(cut), Is.EqualTo(tab));
    }

    [Test]
    public async Task PickingATab_PutsItInTheUrl()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-tab"), Has.Count.EqualTo(5)));

        cut.Find(".nornis-tab[data-tab=export]").Click();

        var nav = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-danger"), Has.Count.EqualTo(1)));
        Assert.That(nav.Uri, Does.EndWith("/admin?tab=export"));
    }

    [Test]
    public async Task ANameTypedOnOneTab_SurvivesAnother_AndAnotherTabsSaveLeavesItOut()
    {
        var cut = await RenderAsync();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-settings-world input"), Is.Not.Empty));

        cut.Find(".nornis-settings-world input").Input("Black Harb");
        cut.Find(".nornis-tab[data-tab=public]").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-settings-publicpage"), Has.Count.EqualTo(1)));
        cut.Find(".nornis-settings-publicpage .nornis-settings-save").Click();

        cut.WaitForAssertion(() => Assert.That(_handler.Puts, Has.Count.EqualTo(1)));
        Assert.That(_handler.Puts[0], Does.Contain("\"name\":null").And.Contain("\"publicSlug\":\"black-harbor\""));

        cut.Find(".nornis-tab[data-tab=world]").Click();
        cut.WaitForAssertion(() => Assert.That(
            cut.Find(".nornis-settings-world input").GetAttribute("value"), Is.EqualTo("Black Harb")));
    }

    [Test]
    public async Task APlayer_SeesGmsOnly_AndNoTabs()
    {
        _handler.Role = "Player";

        var cut = await RenderAsync();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("GMs only")));
        Assert.That(cut.FindAll(".nornis-tab"), Is.Empty);
    }

    [TestCase("GM", true)]
    [TestCase("Player", false)]
    public async Task InviteLinks_AreOnTheMembersPage_ForTheGmOnly(string role, bool sees)
    {
        _handler.Role = role;
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();

        var cut = Render<MembersPage>();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-invites").Count > 0, Is.EqualTo(sees)));
        if (!sees)
        {
            Assert.That(_handler.Requests, Has.None.EndsWith("/invites"));
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Guid _worldId = Guid.NewGuid();

        public string Role { get; set; } = "GM";
        public List<string> Requests { get; } = [];
        public List<string> Puts { get; } = [];

        private string World => $$"""
            {"id":"{{_worldId}}","name":"Black Harbor","description":"A port.","gameSystem":null,"myRole":"{{Role}}",
             "publicSlug":"black-harbor","publicAccessEnabled":true}
            """;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requests.Add(path);
            if (request.Method == HttpMethod.Put && path == $"/api/worlds/{_worldId}")
            {
                Puts.Add(await request.Content!.ReadAsStringAsync(ct));
                return Json(World);
            }

            return path switch
            {
                "/api/worlds" => Json($"[{World}]"),
                _ when path.EndsWith("/invites", StringComparison.Ordinal) => Json("[]"),
                _ when path.EndsWith("/members", StringComparison.Ordinal) => Json("[]"),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
