using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Rendering;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Authentication;
using Nornis.Web.Components.Layout;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The nav's badge poll stands down while the tab is in the background and refreshes on the way
/// back. <see cref="NavActivityCadenceTests"/> covers the rules; this covers the wiring, which is
/// the half that can be right in isolation and still never run — the browser calls
/// <c>OnTabVisibilityChanged</c> through JS interop, and nothing in the C# type system says it is
/// reachable.
/// </summary>
[TestFixture]
// A BunitContext is single-use: NUnit's default one-instance-per-fixture would hand the second
// test an already-disposed renderer.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class NavMenuTabVisibilityTests : BunitContext
{
    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();

        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var authSession = new AuthSessionState();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, viewAs, signal, authSession);

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(authSession);
        Services.AddSingleton(api);
        Services.AddSingleton(new AuthFeature(false));
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        // Both of these resolve from the provider, so they have to come after every registration —
        // touching either one seals it.
        //
        // NavMenu talks to localStorage and the visibility watcher on first render, and it
        // deliberately does nothing during the static prerender pass — no world load, no poll, no
        // visibility watcher — so the interactive renderer is the only one under test.
        JSInterop.Mode = JSRuntimeMode.Loose;

        // Loose mode answers every unconfigured call with default(T) — which for this one is
        // false, so the component would boot believing it is already in a background tab and the
        // "going to the background" test below would be asserting a no-op. Say foreground
        // explicitly; the hidden-at-boot case is a different arrangement, not the default.
        JSInterop.Setup<bool>("nornisNav.isTabVisible").SetResult(true);

        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<NavMenu> RenderNavAsync()
    {
        var cut = Render<NavHost>();

        // Let the boot settle: EnsureLoadedAsync, the first activity read, and the visibility
        // subscription all run off the render.
        await WaitForQuietAsync();
        return cut.FindComponent<NavMenu>().Instance;
    }

    /// <summary>MudMenu inside NavMenu needs a popover provider somewhere above it.</summary>
    private sealed class NavHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<NavMenu>(1);
            builder.CloseComponent();
        }
    }

    /// <summary>
    /// Gives queued continuations a chance to run. The component's own work is awaited internally,
    /// so there is no completion signal to hook — but every path under test is a handful of
    /// already-resolved tasks, not a timer.
    /// </summary>
    private static async Task WaitForQuietAsync()
    {
        for (var i = 0; i < 10; i++)
        {
            await Task.Yield();
            await Task.Delay(5);
        }
    }

    [Test]
    public async Task GoingToTheBackground_FetchesNothing()
    {
        var nav = await RenderNavAsync();
        var before = _handler.ActivityRequests;

        await nav.OnTabVisibilityChanged(false);
        await WaitForQuietAsync();

        Assert.That(_handler.ActivityRequests, Is.EqualTo(before),
            "leaving the tab is not a reason to read anything");
    }

    [Test]
    public async Task ComingBackToTheForeground_RefreshesTheBadges()
    {
        // The behaviour that makes standing the poll down safe. Without it the badges would show
        // whatever was true when the user left, for up to a full idle interval after they return —
        // a strictly worse experience than the always-poll it replaced.
        var nav = await RenderNavAsync();

        await nav.OnTabVisibilityChanged(false);
        await WaitForQuietAsync();
        var afterHiding = _handler.ActivityRequests;

        await nav.OnTabVisibilityChanged(true);
        await WaitForQuietAsync();

        Assert.That(_handler.ActivityRequests, Is.EqualTo(afterHiding + 1),
            "returning to the tab must re-read the badges immediately");
    }

    [Test]
    public async Task RepeatedBackgroundEvents_DoNotEachCostARequest()
    {
        // visibilitychange fires on more than a foreground/background swap, and the handler is
        // reachable from the browser at any rate it likes.
        var nav = await RenderNavAsync();

        await nav.OnTabVisibilityChanged(true);
        await nav.OnTabVisibilityChanged(true);
        await WaitForQuietAsync();
        var baseline = _handler.ActivityRequests;

        await nav.OnTabVisibilityChanged(false);
        await nav.OnTabVisibilityChanged(false);
        await nav.OnTabVisibilityChanged(false);
        await WaitForQuietAsync();

        Assert.That(_handler.ActivityRequests, Is.EqualTo(baseline));
    }

    [Test]
    public async Task Disposing_UnhooksTheBrowserListener()
    {
        // A listener left attached keeps calling into a disposed component for the life of the
        // page. Blazor Server re-renders the layout, so this is not hypothetical.
        var nav = await RenderNavAsync();

        await nav.DisposeAsync();

        Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "nornisNav.unwatchVisibility"),
            Is.True, "the visibilitychange handler must be removed with the component");
    }

    [Test]
    public async Task Booting_AsksTheBrowserWhetherTheTabIsAlreadyHidden()
    {
        // visibilitychange only fires on a CHANGE. A tab restored with the session, or opened
        // with a ctrl-click, is already in the background when the circuit goes live — and would
        // otherwise poll at full rate having never been looked at.
        await RenderNavAsync();

        Assert.That(JSInterop.Invocations.Any(i => i.Identifier == "nornisNav.isTabVisible"),
            Is.True);
    }

    [Test]
    public async Task BootingHidden_IsBelieved_NotJustAsked()
    {
        // Asking and discarding the answer would pass the test above. What proves the answer was
        // applied: a component that booted hidden treats a later "hidden" event as no change, so
        // the return that follows is a real transition and refreshes.
        JSInterop.Setup<bool>("nornisNav.isTabVisible").SetResult(false);

        var nav = await RenderNavAsync();
        var afterBoot = _handler.ActivityRequests;

        await nav.OnTabVisibilityChanged(false);
        await WaitForQuietAsync();
        Assert.That(_handler.ActivityRequests, Is.EqualTo(afterBoot),
            "already hidden at boot, so this is not a transition");

        await nav.OnTabVisibilityChanged(true);
        await WaitForQuietAsync();
        Assert.That(_handler.ActivityRequests, Is.EqualTo(afterBoot + 1));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public int ActivityRequests { get; private set; }

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
                ActivityRequests++;
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
