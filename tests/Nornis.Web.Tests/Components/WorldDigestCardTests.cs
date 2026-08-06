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
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The digest card renders one of two audiences' pages, so what each role sees is an
/// authorization question: the generate/refresh/preview affordances are GM-only, and the
/// preview section exists precisely so a GM can check the party rendering — a player must
/// never get the toggle. And because generation is a ~30-60s wait, the tests assert the
/// on-screen state changes (content swaps, button survives failure), not just that a
/// handler ran.
/// </summary>
[TestFixture]
// A BunitContext is single-use; the default one-instance-per-fixture would hand the second test
// an already-disposed renderer.
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class WorldDigestCardTests : BunitContext
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
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, _viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(_viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, _viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<WorldState> SelectWorldAsync(string role)
    {
        _handler.Role = role;
        var worlds = Services.GetRequiredService<WorldState>();
        await worlds.EnsureSelectionRestoredAsync();
        return worlds;
    }

    /// <summary>The card header's tooltip is a popover; the provider has to sit above the card.</summary>
    private sealed class Host : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<WorldDigestCard>(1);
            builder.CloseComponent();
        }
    }

    private static bool HasGenerateButton(IRenderedComponent<Host> cut) =>
        cut.FindAll("button[aria-label='Generate digest']").Count > 0;

    private static bool HasRefreshButton(IRenderedComponent<Host> cut) =>
        cut.FindAll("button[aria-label='Refresh digest']").Count > 0;

    private static bool HasPreviewToggle(IRenderedComponent<Host> cut) =>
        cut.FindAll("button[aria-label='What players see']").Count > 0;

    [Test]
    public async Task AGmWithNoDigest_SeesTheGenerateButton()
    {
        await SelectWorldAsync("GM");
        var cut = Render<Host>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(HasGenerateButton(cut), Is.True);
            Assert.That(cut.Markup, Does.Contain("No digest yet."));
        });
    }

    [Test]
    public async Task APlayerWithNoDigest_GetsTheQuietLineAndNoButton()
    {
        await SelectWorldAsync("Player");
        var cut = Render<Host>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("No recap yet."));
            Assert.That(HasGenerateButton(cut), Is.False, "a player has no generate affordance");
            Assert.That(HasRefreshButton(cut), Is.False);
        });
    }

    [Test]
    public async Task AGmWithADigest_SeesContentRefreshAndThePlayersPreview()
    {
        _handler.HasDigest = true;
        await SelectWorldAsync("GM");
        var cut = Render<Host>();

        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("The GM digest body"));
            Assert.That(HasRefreshButton(cut), Is.True);
            Assert.That(HasPreviewToggle(cut), Is.True);
        });

        // The preview starts collapsed; opening it renders the party recap for the GM to check.
        Assert.That(cut.Markup, Does.Not.Contain("The party recap body"));
        await cut.Find("button[aria-label='What players see']").ClickAsync(new());
        Assert.That(cut.Markup, Does.Contain("The party recap body"));
    }

    [Test]
    public async Task APlayerWithADigest_SeesTheRecapAlone()
    {
        _handler.HasDigest = true;
        await SelectWorldAsync("Player");
        var cut = Render<Host>();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("The party recap body")));
        Assert.That(HasRefreshButton(cut), Is.False, "player view must not show a GM-only control");
        Assert.That(HasPreviewToggle(cut), Is.False, "the preview of what players see IS what a player sees");
        Assert.That(cut.Markup, Does.Not.Contain("The GM digest body"));
    }

    [Test]
    public async Task Generating_PostsAndTheCardRerendersWithTheNewDigest()
    {
        await SelectWorldAsync("GM");
        var cut = Render<Host>();
        cut.WaitForAssertion(() => Assert.That(HasGenerateButton(cut), Is.True));

        await cut.Find("button[aria-label='Generate digest']").ClickAsync(new());

        Assert.That(_handler.GenerateCalls, Is.EqualTo(1), "the click must reach the generate endpoint");
        cut.WaitForAssertion(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Freshly generated digest"),
                "the returned digest must land on screen, not just in a field");
            Assert.That(HasGenerateButton(cut), Is.False, "the empty state is gone");
            Assert.That(HasRefreshButton(cut), Is.True);
        });
    }

    [Test]
    public async Task AFailedGeneration_KeepsTheButtonAndSaysWhatHappened()
    {
        await SelectWorldAsync("GM");
        _handler.GenerateStatus = HttpStatusCode.BadRequest;
        var cut = Render<Host>();
        cut.WaitForAssertion(() => Assert.That(HasGenerateButton(cut), Is.True));

        await cut.Find("button[aria-label='Generate digest']").ClickAsync(new());

        Assert.That(HasGenerateButton(cut), Is.True, "a failed generation must stay retryable");
        Assert.That(cut.Markup, Does.Not.Contain("Freshly generated digest"));

        var snackbar = Services.GetRequiredService<ISnackbar>();
        Assert.That(snackbar.ShownSnackbars.Any(s => $"{s.Message}".Contains("the API said no")), Is.True,
            "silence after a click is indistinguishable from a dead button");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public string Role { get; set; } = "GM";

        /// <summary>Whether GET returns a stored digest or the HasData=false empty state.</summary>
        public bool HasDigest { get; set; }

        public HttpStatusCode GenerateStatus { get; set; } = HttpStatusCode.OK;
        public int GenerateCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Task.FromResult(Json(HttpStatusCode.OK,
                    $$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                      "gameSystem":null,"myRole":"{{Role}}"}]
                    """));
            }

            if (path == $"/api/worlds/{WorldId}/digest" && request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, HasDigest
                    ? DigestJson("## Active storylines\nThe GM digest body")
                    : """{"hasData":false,"generatedAt":null,"content":null,"partyPreview":null}"""));
            }

            if (path == $"/api/worlds/{WorldId}/digest/generate" && request.Method == HttpMethod.Post)
            {
                GenerateCalls++;
                return Task.FromResult(GenerateStatus == HttpStatusCode.OK
                    ? Json(HttpStatusCode.OK, DigestJson("## Active storylines\nFreshly generated digest"))
                    : Problem(GenerateStatus));
            }

            // Continuity and anything else the boot touches: absent is fine.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        /// <summary>The API renders per-role: the GM gets their digest plus the party preview,
        /// a player gets the recap as the content and no preview — mirrored here so the card's
        /// role-dependent affordances are exercised against role-dependent data.</summary>
        private string DigestJson(string gmContent) => Role == "GM"
            ? $$"""
                {"hasData":true,"generatedAt":"2026-08-05T00:00:00+00:00",
                 "content":{{JsonString(gmContent)}},
                 "partyPreview":"## The story so far\nThe party recap body"}
                """
            : """
              {"hasData":true,"generatedAt":"2026-08-05T00:00:00+00:00",
               "content":"## The story so far\nThe party recap body","partyPreview":null}
              """;

        private static string JsonString(string s) => System.Text.Json.JsonSerializer.Serialize(s);

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        // The API's real error dialect ({code, message}), not problem+json: the client
        // deserializes into ApiError, and a shape mismatch yields null members — which the
        // snackbar then silently drops, which is exactly what the error test guards against.
        private static HttpResponseMessage Problem(HttpStatusCode status) => new(status)
        {
            Content = new StringContent(
                """{"code":"validation_error","message":"the API said no"}""",
                Encoding.UTF8, "application/json"),
        };
    }
}
