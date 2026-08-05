using System.Net;
using System.Text;
using System.Text.Json;
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
/// The file-back button is a write affordance on the Ask surfaces, so who can see it is an
/// authorization question — same discipline as GmNoteButton, including disappearing while a
/// GM previews as a player. And because "it filed" must be visible, the tests assert the
/// on-screen state change (the button becomes the filed link), not just that a handler ran.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AskFileBackButtonTests : BunitContext
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
        Services.AddSingleton(new GmNoteWriter(api));
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

    private static AskExchange Exchange(Guid? filedSourceId = null) => new()
    {
        Question = "Who hired the raiders?",
        Answer = "The evidence points to Captain Voss.",
        Confidence = "High",
        FiledSourceId = filedSourceId,
    };

    /// <summary>The tooltip is a popover; the provider has to sit above the button.</summary>
    private sealed class Host : ComponentBase
    {
        [Parameter] public AskExchange Exchange { get; set; } = default!;
        [Parameter] public EventCallback<Guid> Filed { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<MudPopoverProvider>(0);
            builder.CloseComponent();
            builder.OpenComponent<AskFileBackButton>(1);
            builder.AddComponentParameter(2, nameof(AskFileBackButton.Exchange), Exchange);
            builder.AddComponentParameter(3, nameof(AskFileBackButton.Filed), Filed);
            builder.CloseComponent();
        }
    }

    private IRenderedComponent<Host> RenderButton(AskExchange exchange, Action<Guid>? onFiled = null) =>
        Render<Host>(ps =>
        {
            ps.Add(h => h.Exchange, exchange);
            if (onFiled is not null)
            {
                ps.Add(h => h.Filed, onFiled);
            }
        });

    private static bool HasFileButton(IRenderedComponent<Host> cut) =>
        cut.FindAll("[aria-label='File this answer']").Count > 0;

    private static bool HasFiledLink(IRenderedComponent<Host> cut) =>
        cut.FindAll("[aria-label='Filed as a source']").Count > 0;

    [Test]
    public async Task AGm_SeesTheFileButton()
    {
        await SelectWorldAsync("GM");

        Assert.That(HasFileButton(RenderButton(Exchange())), Is.True);
    }

    [Test]
    public async Task APlayer_DoesNot()
    {
        await SelectWorldAsync("Player");

        Assert.That(HasFileButton(RenderButton(Exchange())), Is.False);
    }

    [Test]
    public async Task AGmViewingAsAPlayer_LosesTheButtonAndGetsItBack()
    {
        var worlds = await SelectWorldAsync("GM");
        var cut = RenderButton(Exchange());

        await cut.InvokeAsync(() => worlds.SetViewingAsPlayer(true));
        Assert.That(HasFileButton(cut), Is.False, "player view must not show a GM-only control");

        await cut.InvokeAsync(() => worlds.SetViewingAsPlayer(false));
        Assert.That(HasFileButton(cut), Is.True, "leaving player view restores it without a reload");
    }

    [Test]
    public async Task AnAlreadyFiledAnswer_ShowsTheFiledLinkInstead()
    {
        await SelectWorldAsync("GM");
        var sourceId = Guid.NewGuid();

        var cut = RenderButton(Exchange(filedSourceId: sourceId));

        Assert.That(HasFileButton(cut), Is.False, "an answer files once");
        Assert.That(HasFiledLink(cut), Is.True);
        Assert.That(cut.Find("[aria-label='Filed as a source']").GetAttribute("href"),
            Is.EqualTo($"/sources/{sourceId}"));
    }

    [Test]
    public async Task Filing_CreatesAndQueuesTheSource_AndTheButtonBecomesTheFiledLink()
    {
        await SelectWorldAsync("GM");
        var exchange = Exchange();
        Guid? filedWorld = null;
        var cut = RenderButton(exchange, onFiled: id => filedWorld = id);

        await cut.Find("[aria-label='File this answer']").ClickAsync(new());

        var created = _handler.CreatedBody!.RootElement;
        Assert.That(created.GetProperty("visibility").GetString(), Is.EqualTo("GMOnly"));
        Assert.That(created.GetProperty("body").GetString(), Does.Contain("Who hired the raiders?"));
        Assert.That(created.GetProperty("body").GetString(), Does.Contain("The evidence points to Captain Voss."));
        Assert.That(_handler.MarkedReadySourceId, Is.EqualTo(_handler.NewSourceId),
            "an answer that is never marked ready is never read");

        Assert.That(exchange.FiledSourceId, Is.EqualTo(_handler.NewSourceId));
        Assert.That(filedWorld, Is.EqualTo(_handler.WorldId), "the owner persists history for the filed world");
        Assert.That(HasFileButton(cut), Is.False, "the affordance must visibly change state");
        Assert.That(HasFiledLink(cut), Is.True);

        var snackbar = Services.GetRequiredService<ISnackbar>();
        Assert.That(snackbar.ShownSnackbars.Any(s => $"{s.Message}".Contains("Filed")), Is.True,
            "the GM is told it worked without having to spot the icon change");
    }

    [Test]
    public async Task AFailedFiling_KeepsTheButtonAndSaysWhatHappened()
    {
        await SelectWorldAsync("GM");
        _handler.CreateStatus = HttpStatusCode.BadRequest;
        var exchange = Exchange();
        var cut = RenderButton(exchange);

        await cut.Find("[aria-label='File this answer']").ClickAsync(new());

        Assert.That(exchange.FiledSourceId, Is.Null);
        Assert.That(HasFileButton(cut), Is.True, "a failed filing must stay retryable");
        Assert.That(HasFiledLink(cut), Is.False);

        var snackbar = Services.GetRequiredService<ISnackbar>();
        Assert.That(snackbar.ShownSnackbars.Count(), Is.EqualTo(1),
            "silence after a click is indistinguishable from a dead button");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public string Role { get; set; } = "GM";
        public HttpStatusCode CreateStatus { get; set; } = HttpStatusCode.Created;

        public Guid NewSourceId { get; } = Guid.NewGuid();
        public JsonDocument? CreatedBody { get; private set; }
        public Guid? MarkedReadySourceId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Json(HttpStatusCode.OK,
                    $$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                      "gameSystem":null,"myRole":"{{Role}}"}]
                    """);
            }

            if (path == $"/api/worlds/{WorldId}/sources")
            {
                CreatedBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                return CreateStatus == HttpStatusCode.Created
                    ? Json(CreateStatus, SourceJson(NewSourceId))
                    : Problem(CreateStatus);
            }

            if (path == $"/api/worlds/{WorldId}/sources/{NewSourceId}/ready")
            {
                MarkedReadySourceId = NewSourceId;
                return Json(HttpStatusCode.OK, SourceJson(NewSourceId));
            }

            // Continuity and anything else the boot touches: absent is fine.
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string SourceJson(Guid id) =>
            $$"""
            {"id":"{{id}}","worldId":"{{WorldId}}","type":"GMNote","title":"Filed from Ask",
             "body":null,"uri":null,"occurredAt":null,"createdAt":"2026-08-05T00:00:00+00:00",
             "visibility":"GMOnly","processingStatus":"Draft"}
            """;

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
