using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// The Key art card (feature 42) shows what the API answers: who sees the art, Generate waiting on
/// a key and a brief, options picked by clicking one, and a change telling the chrome. Its limits
/// and anchor names mirror the Domain's and the Application's, which the Web can't reference.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class KeyArtCardTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
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

    private static KeyArtStateDto State(
        KeyArtDto? art = null, string? brief = null, IReadOnlyList<Guid>? options = null, Guid? chosen = null,
        bool canGenerate = true, string? blocker = null, int used = 0, string? warning = null) =>
        new(art, brief, options ?? [], chosen, used, 8, canGenerate, blocker, warning);

    private static KeyArtDto Art(string source = "Uploaded", string anchor = "Centre", int focus = 50) =>
        new(source, anchor, "image/png", DateTimeOffset.Parse("2026-11-07T20:00:00Z"), focus);

    private IRenderedComponent<KeyArtCard> RenderCard(KeyArtStateDto state, bool isPublic = false)
    {
        _handler.Answer = state;
        Render<MudBlazor.MudPopoverProvider>();
        return Render<KeyArtCard>(p => p
            .Add(x => x.WorldId, WorldId)
            .Add(x => x.PublicAccessEnabled, isPublic)
            .Add(x => x.OnChanged, () => _changed++));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TheCard_SaysWhoSeesTheArt(bool isPublic)
    {
        var cut = RenderCard(State(), isPublic);

        var text = cut.Find(".nornis-keyart-visibility").TextContent;
        Assert.That(text, Does.Contain("Everyone in this world"));
        Assert.That(text.Contains("anyone with the public link"), Is.EqualTo(isPublic));
    }

    [Test]
    public void WithNoArt_ThePreviewSaysHomeShowsTheMap()
    {
        var cut = RenderCard(State());

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-keyart-preview-empty").TextContent, Does.Contain("world map"));
            Assert.That(cut.FindAll(".nornis-keyart-remove"), Is.Empty);
        });
    }

    [Test]
    public void WithArt_ThePreviewUsesTheStampedRoute_AndTheFocus()
    {
        var cut = RenderCard(State(art: Art(anchor: "Bottom", focus: 100)));

        var style = cut.Find(".nornis-keyart-preview-image").GetAttribute("style")!;
        Assert.Multiple(() =>
        {
            Assert.That(style, Does.Contain($"/world-art/{WorldId}?v="));
            Assert.That(style, Does.Contain("center 100%"));
            Assert.That(cut.Find(".nornis-keyart-current").TextContent, Does.Contain("Your upload"));
        });
    }

    [Test]
    public void WithNoKey_GenerateIsDisabled_AndTheCardSaysToAddAnOpenAiKey()
    {
        var cut = RenderCard(State(brief: "A coast at dusk.", canGenerate: false,
            blocker: "Add an OpenAI key to generate key art. You can still upload your own."));

        cut.Find(".nornis-keyart-generate-open").Click();

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-keyart-generate").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find(".nornis-keyart-blocker").TextContent, Does.Contain("OpenAI key"));
            Assert.That(cut.Find(".nornis-cover-upload-input").HasAttribute("disabled"), Is.False, "uploading still works");
        });
    }

    [Test]
    public void Generate_SendsTheBriefAsEdited_AndShowsTheOptions_WithoutTellingTheChrome()
    {
        var cut = RenderCard(State(brief: "A coast at dusk."));
        cut.Find(".nornis-keyart-generate-open").Click();
        cut.Find(".nornis-cover-brief textarea").Input("A coast at midnight.");
        _handler.Answer = State(brief: "A coast at midnight.", options: [OptionB, OptionA], used: 2);

        cut.Find(".nornis-keyart-generate").Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-keyart-option"), Has.Count.EqualTo(2)));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Last, Is.EqualTo($"POST /api/worlds/{WorldId}/key-art/generate"));
            Assert.That(_handler.LastBody, Does.Contain("A coast at midnight."));
            Assert.That(cut.Find(".nornis-keyart-option img").GetAttribute("src"), Is.EqualTo($"/world-art/{WorldId}/options/{OptionB}"));
            Assert.That(cut.Find(".nornis-keyart-count").TextContent, Does.Contain("2 of 8"));
            Assert.That(_changed, Is.Zero, "options aren't the art: nothing on Home changed");
        });
    }

    [Test]
    public void PickingAnOption_MakesItTheArt_AndTellsTheChrome()
    {
        var cut = RenderCard(State(options: [OptionB, OptionA]));
        _handler.Answer = State(art: Art("Generated"), options: [OptionB, OptionA], chosen: OptionA);

        cut.FindAll(".nornis-keyart-option")[1].Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-keyart-preview-image"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Last, Is.EqualTo($"POST /api/worlds/{WorldId}/key-art/choose"));
            Assert.That(_handler.LastBody, Does.Contain(OptionA.ToString()));
            Assert.That(cut.Find(".nornis-keyart-current").TextContent, Does.Contain("Generated with AI"));
            Assert.That(cut.Find(".nornis-cover-option-chosen").HasAttribute("disabled"), Is.True);
            Assert.That(_changed, Is.EqualTo(1));
        });
    }

    [Test]
    public void Remove_TellsTheChrome()
    {
        var cut = RenderCard(State(art: Art()));
        _handler.Answer = State();

        cut.Find(".nornis-keyart-remove").Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-keyart-preview-empty"), Has.Count.EqualTo(1)));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Last, Is.EqualTo($"DELETE /api/worlds/{WorldId}/key-art"));
            Assert.That(_changed, Is.EqualTo(1));
        });
    }

    [Test]
    public void AWarning_AndARefusal_AreShownInTheApisWords()
    {
        var cut = RenderCard(State(art: Art(), brief: "A coast.", warning: "This image is 800 pixels wide, so it may look soft."));
        Assert.That(cut.Find(".nornis-keyart-warning").TextContent, Does.Contain("800 pixels wide"));

        cut.Find(".nornis-keyart-generate-open").Click();
        _handler.Refusal = """{"code":"key_art_cap","message":"This world has generated its 8 images for this month."}""";
        cut.Find(".nornis-keyart-generate").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-keyart-error").TextContent, Does.Contain("8 images for this month")));
    }

    [Test]
    public void TheBriefLimit_IsTheDomains()
    {
        var source = Read("src", "Nornis.Domain", "Models", "AdventureCover.cs");
        var match = Regex.Match(source, @"public const int MaxBriefChars\s*=\s*(?<n>[\d_]+);");

        Assert.That(match.Success, Is.True);
        Assert.That(Read("src", "Nornis.Domain", "Models", "WorldKeyArt.cs"), Does.Contain("MaxBriefChars = AdventureCover.MaxBriefChars;"));
        Assert.That(KeyArtCard.MaxBriefChars, Is.EqualTo(int.Parse(match.Groups["n"].Value.Replace("_", ""))));
    }

    [Test]
    public void TheUploadLimit_IsKeyArtOptionsDefault()
    {
        var source = Read("src", "Nornis.Application", "Configuration", "KeyArtOptions.cs");

        Assert.That(source, Does.Contain("public int MaxUploadBytes { get; set; } = 10 * 1024 * 1024;"));
        Assert.That(KeyArtCard.MaxUploadBytes, Is.EqualTo(10 * 1024 * 1024));
    }

    [Test]
    public void TheAnchors_AreKeyArtAnchorsValues()
    {
        var source = Read("src", "Nornis.Domain", "Enums", "KeyArtAnchor.cs");
        var values = Regex.Matches(source, @"^\s*(?<v>[A-Z]\w*),?\s*$", RegexOptions.Multiline).Select(m => m.Groups["v"].Value);

        Assert.That(KeyArtCard.Anchors.Select(a => a.Value), Is.EquivalentTo(values));
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public KeyArtStateDto Answer { get; set; } = null!;
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
