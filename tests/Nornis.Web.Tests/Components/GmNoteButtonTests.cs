using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The GM-note button is the one affordance that writes to the record from anywhere in the app,
/// so who can see it is an authorization question, not a cosmetic one. It must also disappear
/// while a GM is previewing the world as a player — that mode exists to show what a player
/// sees, and a GM-only control left on screen makes it lie.
/// </summary>
[TestFixture]
// A BunitContext is single-use; the default one-instance-per-fixture would hand the second test
// an already-disposed renderer.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class GmNoteButtonTests : BunitContext
{
    private ViewAsState _viewAs = null!;
    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        _viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, _viewAs, signal);

        Services.AddMudServices();
        Services.AddSingleton(_viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(new GmNoteContext());
        Services.AddSingleton(new GmNoteWriter(api));
        Services.AddSingleton(sp => new WorldState(api, _viewAs, sp.GetRequiredService<IJSRuntime>()));

        // Resolves from the provider, so it has to come after every registration.
        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    /// <summary>Selects the stub's world, which is what <c>EffectiveRole</c> reads from.</summary>
    private async Task<WorldState> SelectWorldAsync(string role)
    {
        _handler.Role = role;
        var worlds = Services.GetRequiredService<WorldState>();
        await worlds.EnsureSelectionRestoredAsync();
        return worlds;
    }

    private static bool HasButton(IRenderedComponent<ButtonHost> cut) =>
        cut.FindAll("button[aria-label='Write a GM note']").Count > 0;

    /// <summary>
    /// The button's tooltip is a popover, and opening the note dialog needs a dialog provider;
    /// both have to sit above the button.
    /// </summary>
    private sealed class ButtonHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<MudDialogProvider>(1);
            builder.CloseComponent();
            builder.OpenComponent<GmNoteButton>(2);
            builder.CloseComponent();
        }
    }

    [Test]
    public async Task AGm_SeesTheButton()
    {
        await SelectWorldAsync("GM");

        Assert.That(HasButton(Render<ButtonHost>()), Is.True);
    }

    [Test]
    public async Task APlayer_DoesNot()
    {
        await SelectWorldAsync("Player");

        Assert.That(HasButton(Render<ButtonHost>()), Is.False);
    }

    [Test]
    public async Task AnObserver_DoesNot()
    {
        await SelectWorldAsync("Observer");

        Assert.That(HasButton(Render<ButtonHost>()), Is.False);
    }

    [Test]
    public async Task WithNoWorldSelected_ThereIsNothingToWriteAbout()
    {
        // Rendered without ever restoring a selection: the button has no world to file against.
        Assert.That(HasButton(Render<ButtonHost>()), Is.False);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Opens the dialog and returns it. The click is deliberately not awaited: the handler sits
    /// on <c>await dialog.Result</c> until the dialog closes, so awaiting it here would hang.
    /// </summary>
    private static IRenderedComponent<GmNoteDialog> OpenDialog(IRenderedComponent<ButtonHost> cut)
    {
        _ = cut.Find("button[aria-label='Write a GM note']").ClickAsync(new());
        return cut.WaitForComponent<GmNoteDialog>();
    }

    [Test]
    public async Task TheDialog_IsToldWhatTheGmWasReading()
    {
        // The headline behaviour: a note written on Captain Voss's page is about Captain Voss.
        // Without this the note still saves, so nothing else here would notice it going missing.
        await SelectWorldAsync("GM");
        Services.GetRequiredService<GmNoteContext>().Set("Captain Voss");

        var dialog = OpenDialog(Render<ButtonHost>());

        Assert.That(dialog.Instance.Subject, Is.EqualTo("Captain Voss"));
    }

    [Test]
    public async Task WithNothingInContext_TheNoteStandsOnItsOwn()
    {
        await SelectWorldAsync("GM");

        var dialog = OpenDialog(Render<ButtonHost>());

        Assert.That(dialog.Instance.Subject, Is.Null);
    }

    [Test]
    public async Task AGmViewingAsAPlayer_LosesTheButtonAndGetsItBack()
    {
        var worlds = await SelectWorldAsync("GM");
        var cut = Render<ButtonHost>();

        await cut.InvokeAsync(() => worlds.SetViewingAsPlayer(true));
        Assert.That(HasButton(cut), Is.False, "player view must not show a GM-only control");

        await cut.InvokeAsync(() => worlds.SetViewingAsPlayer(false));
        Assert.That(HasButton(cut), Is.True, "leaving player view restores it without a reload");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public string Role { get; set; } = "GM";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/worlds")
            {
                return Task.FromResult(Json(
                    $$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                      "gameSystem":null,"myRole":"{{Role}}"}]
                    """));
            }

            // Continuity and anything else the boot touches: absent is fine.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
