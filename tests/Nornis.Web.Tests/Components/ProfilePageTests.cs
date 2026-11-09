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
using ProfilePage = Nornis.Web.Components.Pages.Profile;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// Your settings (Nornis 3.24): three tabs with the shown one in the URL; a sign-in link or a
/// Bibliotaph connect that comes back here opens Account, where its outcome is shown.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ProfilePageTests : BunitContext
{
    [SetUp]
    public void SetUp()
    {
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));
        Services.AddSingleton(sp => new ThemeState(sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    [TestCase(null, null, null, "world")]
    [TestCase("AI", null, null, "ai")]
    [TestCase("account", null, null, "account")]
    [TestCase("nonsense", null, null, "world")]
    [TestCase(null, "linked", null, "account")]
    [TestCase(null, null, "connected", "account")]
    [TestCase("world", "linked", null, "world")]
    public void TheTab_IsTheUrlsOrElseWhereAnOutcomeIsShown(string? tab, string? signIn, string? bibliotaph, string expected) =>
        Assert.That(ProfilePage.TabFor(tab, signIn, bibliotaph), Is.EqualTo(expected));

    private async Task<IRenderedComponent<ProfilePage>> RenderAsync(string url)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(url);
        await Services.GetRequiredService<WorldState>().EnsureSelectionRestoredAsync();
        Render<MudPopoverProvider>();
        return Render<ProfilePage>();
    }

    private static string ActiveTab(IRenderedComponent<ProfilePage> cut) =>
        cut.Find(".nornis-profile-tabs .nornis-tab-active").GetAttribute("data-tab")!;

    [Test]
    public async Task ThePage_OpensOnThisWorld_NamingIt()
    {
        var cut = await RenderAsync("/profile");

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-profile-world-note"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(ActiveTab(cut), Is.EqualTo("world"));
            Assert.That(cut.Find(".nornis-profile-world-note").TextContent, Does.Contain("Black Harbor"));
            Assert.That(cut.Markup, Does.Contain("Display name"));
            Assert.That(cut.FindAll("#ai-keys"), Is.Empty, "AI keys are on their own tab");
        });
    }

    [Test]
    public async Task TheAiTab_HoldsTheKeysAndTheModels()
    {
        var cut = await RenderAsync("/profile?tab=ai");

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("#ai-keys"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll("#ai-models"), Has.Count.EqualTo(1));
            Assert.That(cut.Markup, Does.Not.Contain("Display name"));
        });
    }

    [Test]
    public async Task ComingBackFromASignInLink_OpensAccount_WithItsOutcome()
    {
        var cut = await RenderAsync("/profile?signin=linked");

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Sign-ins")));
        Assert.Multiple(() =>
        {
            Assert.That(ActiveTab(cut), Is.EqualTo("account"));
            Assert.That(cut.Markup, Does.Contain("Appearance"));
        });
    }

    [Test]
    public async Task PickingATab_PutsItInTheUrl()
    {
        var cut = await RenderAsync("/profile");
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-tab"), Has.Count.EqualTo(3)));

        cut.Find(".nornis-tab[data-tab=ai]").Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("#ai-keys"), Has.Count.EqualTo(1)));
        Assert.That(Services.GetRequiredService<NavigationManager>().Uri, Does.EndWith("/profile?tab=ai"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Guid _worldId = Guid.NewGuid();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            return Task.FromResult(path switch
            {
                "/api/worlds" => Json($$"""
                    [{"id":"{{_worldId}}","name":"Black Harbor","description":null,"gameSystem":null,"myRole":"Player"}]
                    """),
                _ when path.EndsWith("/characters", StringComparison.Ordinal) => Json("[]"),
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
