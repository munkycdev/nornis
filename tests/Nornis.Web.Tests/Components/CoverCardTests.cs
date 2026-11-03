using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Cover card (feature 38): it shows what the API answers. Generate waits on a key and a
/// brief, the options are picked by clicking one, and a change that can record AI use tells the
/// page so the Book details card refreshes.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CoverCardTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid AdventureId = Guid.NewGuid();
    private static readonly Guid OptionA = Guid.NewGuid();
    private static readonly Guid OptionB = Guid.NewGuid();
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private StubHandler _handler = null!;
    private int _changed;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, new ViewAsState(), new ActivitySignal(),
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(api);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private static CoverStateDto State(
        CoverDto? cover = null, string? brief = null, IReadOnlyList<Guid>? options = null, Guid? chosen = null,
        bool canGenerate = true, string? blocker = null, int used = 0) =>
        new(cover, brief, options ?? [], chosen, used, 12, canGenerate, blocker, null);

    private IRenderedComponent<CoverCard> RenderCard(CoverStateDto state)
    {
        _handler.Answer = state;
        Render<MudBlazor.MudPopoverProvider>();
        return Render<CoverCard>(p => p
            .Add(x => x.WorldId, WorldId)
            .Add(x => x.AdventureId, AdventureId)
            .Add(x => x.OnChanged, () => _changed++));
    }

    [Test]
    public void WithNoArt_TheCardSaysTheBookletIsTypeset()
    {
        var cut = RenderCard(State());

        Assert.That(cut.Find(".nornis-cover-empty").TextContent, Does.Contain("typeset cover"));
        Assert.That(cut.FindAll(".nornis-cover-thumb"), Is.Empty);
    }

    [Test]
    public void WithNoKey_GenerateIsDisabled_AndTheCardSaysToAddAnOpenAiKey()
    {
        var cut = RenderCard(State(brief: "A bathhouse at dusk.", canGenerate: false,
            blocker: "Add an OpenAI key to generate covers. You can still upload your own art."));

        cut.Find(".nornis-cover-generate-open").Click();

        Assert.That(cut.Find(".nornis-cover-generate").HasAttribute("disabled"), Is.True);
        Assert.That(cut.Find(".nornis-cover-blocker").TextContent, Does.Contain("OpenAI key"));
        Assert.That(cut.Find(".nornis-cover-upload-input").HasAttribute("disabled"), Is.False, "uploading still works");
    }

    [Test]
    public void Generate_SendsTheBriefAsEdited_AndShowsTheOptions()
    {
        var cut = RenderCard(State(brief: "A bathhouse at dusk."));
        cut.Find(".nornis-cover-generate-open").Click();
        cut.Find(".nornis-cover-brief textarea").Input("A bathhouse at midnight.");
        _handler.Answer = State(brief: "A bathhouse at midnight.", options: [OptionB, OptionA], used: 2);

        cut.Find(".nornis-cover-generate").Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-cover-option"), Has.Count.EqualTo(2)));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Last, Is.EqualTo("POST /api/worlds/" + WorldId + "/prep/" + AdventureId + "/cover/generate"));
            Assert.That(_handler.LastBody, Does.Contain("A bathhouse at midnight."));
            Assert.That(cut.Find(".nornis-cover-option img").GetAttribute("src"),
                Is.EqualTo($"/prep-covers/{WorldId}/{AdventureId}/options/{OptionB}"));
            Assert.That(cut.Find(".nornis-cover-count").TextContent, Does.Contain("2 of 12"));
            Assert.That(_changed, Is.Zero, "options aren't the cover: nothing about the book changed");
        });
    }

    [Test]
    public void PickingAnOption_MakesItTheCover_AndTellsThePage()
    {
        var cut = RenderCard(State(options: [OptionB, OptionA]));
        var cover = new CoverDto("Generated", "FullBleed", "image/png", DateTimeOffset.Parse("2026-11-03T10:00:00Z"));
        _handler.Answer = State(cover: cover, options: [OptionB, OptionA], chosen: OptionA);

        cut.FindAll(".nornis-cover-option")[1].Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-cover-thumb"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastBody, Does.Contain(OptionA.ToString()));
            Assert.That(cut.Find(".nornis-cover-source").TextContent, Does.Contain("Generated with AI"));
            Assert.That(cut.Find(".nornis-cover-option-chosen").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find(".nornis-cover-thumb").GetAttribute("src"), Does.Contain("?v="));
            Assert.That(_changed, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnApiRefusal_IsShownInTheApisWords()
    {
        var cut = RenderCard(State(brief: "A bathhouse at dusk."));
        cut.Find(".nornis-cover-generate-open").Click();
        _handler.Refusal = """{"code":"cover_cap","message":"This adventure has used its 12 generated images. You can still upload your own art."}""";

        cut.Find(".nornis-cover-generate").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-cover-error").TextContent, Does.Contain("12 generated images")));
    }

    [Test]
    public void TheLayouts_AreTheThreeTheRendererKnows()
    {
        Assert.That(CoverCard.Layouts.Select(l => l.Value), Is.EqualTo((string[])["FullBleed", "Framed", "ClassicModule"]));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public CoverStateDto Answer { get; set; } = null!;
        public string? Refusal { get; set; }
        public string? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = $"{request.Method} {request.RequestUri!.AbsolutePath}";
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (Refusal is not null && request.Method != HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(Refusal, Encoding.UTF8, "application/json") };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(Answer, Web), Encoding.UTF8, "application/json"),
            };
        }
    }
}
