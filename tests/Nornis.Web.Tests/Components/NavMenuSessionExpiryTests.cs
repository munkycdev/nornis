using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Authentication;
using Nornis.Web.Components.Layout;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The 401 storm of 2026-07-27: a circuit whose token could no longer be refreshed kept polling
/// the activity endpoint at full cadence — ~400 unauthorized requests over three hours — and the
/// badge failed silently, so the one person who could fix it never knew. These tests cover the
/// wiring that ends both halves: the poll's 401 flips <see cref="AuthSessionState"/>, the nav
/// shows the state, and a recovered session clears it again without a reload.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class NavMenuSessionExpiryTests : BunitContext
{
    private StubHandler _handler = null!;
    private AuthSessionState _authSession = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        _authSession = new AuthSessionState();

        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            _authSession);

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(_authSession);
        Services.AddSingleton(api);
        Services.AddSingleton(new AuthFeature(false));
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("nornisNav.isTabVisible").SetResult(true);

        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<NavHost>> RenderNavAsync()
    {
        var cut = Render<NavHost>();
        await WaitForQuietAsync();
        return cut;
    }

    /// <summary>MudMenu inside NavMenu needs a popover provider somewhere above it.</summary>
    private sealed class NavHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudBlazor.MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<NavMenu>(1);
            builder.CloseComponent();
        }
    }

    private static async Task WaitForQuietAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            await Task.Delay(5);
        }
    }

    [Test]
    public async Task AnUnauthorizedPoll_ShowsTheSessionExpiredBanner()
    {
        // The person whose poll is failing is the one person the failure was invisible to.
        _handler.ActivityRespondsUnauthorized = true;

        var cut = await RenderNavAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authSession.Expired, Is.True,
                "the poll's own 401 is what marks the session expired");
            Assert.That(cut.Markup, Does.Contain("Your session has expired"));
            Assert.That(cut.Markup, Does.Contain("Sign in again"));
        });
    }

    [Test]
    public async Task AWorkingSession_ShowsNoBanner()
    {
        var cut = await RenderNavAsync();

        Assert.That(cut.Markup, Does.Not.Contain("Your session has expired"));
    }

    [Test]

    [Category("Authorization")]
    public async Task ARecoveredSession_ClearsTheBannerWithoutAReload()
    {
        // A failed token refresh can be transient. The slow probe (or any successful call) goes
        // back through the refresher; one working answer must put the nav back to normal, or the
        // banner cries wolf after every blip.
        _handler.ActivityRespondsUnauthorized = true;
        var cut = await RenderNavAsync();
        Assert.That(cut.Markup, Does.Contain("Your session has expired"), "arranged: expired");

        _handler.ActivityRespondsUnauthorized = false;

        // Returning to the tab forces a refresh — the cheapest in-test stand-in for the probe.
        var nav = cut.FindComponent<NavMenu>().Instance;
        await nav.OnTabVisibilityChanged(false);
        await nav.OnTabVisibilityChanged(true);
        await WaitForQuietAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_authSession.Expired, Is.False);
            Assert.That(cut.Markup, Does.Not.Contain("Your session has expired"));
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public bool ActivityRespondsUnauthorized { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Task.FromResult(Json(
                    $$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,"gameSystem":null,"myRole":"GM"}]
                    """));
            }

            if (path == $"/api/worlds/{WorldId}/sources/activity")
            {
                if (ActivityRespondsUnauthorized)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                }

                return Task.FromResult(Json(
                    """
                    {"queued":0,"processing":0,"ready":0,"failed":0,"inFlight":0,
                     "pendingProposals":0,"pendingProposalsCapped":false}
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
