using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Typography card on the Book tab: it shows the stored type (or the defaults), its sample
/// follows every change before a save, and a save sends all three with a default as null.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class TypographyCardTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid AdventureId = Guid.NewGuid();

    private StubHandler _handler = null!;
    private AdventureDto? _saved;

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

    private static AdventureDto Adventure(BookTypographyDto? typography) =>
        new(AdventureId, WorldId, null, "The Silver Key", "the-silver-key", "A heist.", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], Typography: typography);

    private IRenderedComponent<TypographyCard> RenderCard(BookTypographyDto? typography = null)
    {
        Render<MudPopoverProvider>();
        return Render<TypographyCard>(p => p
            .Add(x => x.Adventure, Adventure(typography))
            .Add(x => x.OnSaved, (AdventureDto a) => _saved = a));
    }

    private static string Sample(IRenderedComponent<TypographyCard> cut) =>
        cut.Find("[data-testid='typography-sample']").GetAttribute("style")!;

    private static bool SaveDisabled(IRenderedComponent<TypographyCard> cut) =>
        cut.Find("button.nornis-typography-save").HasAttribute("disabled");

    private static Task Choose(IRenderedComponent<TypographyCard> cut, string font) =>
        cut.InvokeAsync(() => cut.FindComponent<MudSelect<string>>().Instance.ValueChanged.InvokeAsync(font));

    private static Task SetNumber(IRenderedComponent<TypographyCard> cut, int index, decimal value) =>
        cut.InvokeAsync(() => cut.FindComponents<MudNumericField<decimal>>()[index].Instance.ValueChanged.InvokeAsync(value));

    [Test]
    public void NothingChosen_ShowsTheDefaults_AndNothingToSave()
    {
        var cut = RenderCard();

        Assert.Multiple(() =>
        {
            Assert.That(Sample(cut), Does.Contain("'Newsreader'").And.Contain("font-size: 10pt").And.Contain("line-height: 1.3"));
            Assert.That(SaveDisabled(cut), Is.True);
            Assert.That(cut.Find("button.nornis-typography-defaults").HasAttribute("disabled"), Is.True, "already the defaults");
        });
    }

    [Test]
    public void AStoredType_IsShownInTheSample()
    {
        var cut = RenderCard(new BookTypographyDto("Lora", 12m, 1.5m));

        Assert.That(Sample(cut), Does.Contain("'Lora'").And.Contain("font-size: 12pt").And.Contain("line-height: 1.5"));
    }

    [Test]
    public async Task AChange_ShowsInTheSampleBeforeASave_AndSaveSendsAllThree_ADefaultAsNull()
    {
        var cut = RenderCard();

        await Choose(cut, "EbGaramond");
        await SetNumber(cut, 0, 11.5m);
        Assert.Multiple(() =>
        {
            Assert.That(Sample(cut), Does.Contain("'EB Garamond'").And.Contain("font-size: 11.5pt"));
            Assert.That(SaveDisabled(cut), Is.False);
        });

        cut.Find("button.nornis-typography-save").Click();

        cut.WaitForAssertion(() => Assert.That(_saved, Is.Not.Null));
        var body = JsonDocument.Parse(_handler.LastPutBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastPutPath, Is.EqualTo($"/api/worlds/{WorldId}/prep/{AdventureId}/typography"));
            Assert.That(body.GetProperty("font").GetString(), Is.EqualTo("EbGaramond"));
            Assert.That(body.GetProperty("size").GetDecimal(), Is.EqualTo(11.5m));
            Assert.That(body.GetProperty("lineSpacing").ValueKind, Is.EqualTo(JsonValueKind.Null), "left at the default");
        });
    }

    [Test]
    public async Task UseTheDefaults_ThenSave_ClearsEveryChoice()
    {
        var cut = RenderCard(new BookTypographyDto("Lora", 12m, 1.5m));

        await cut.InvokeAsync(() => cut.Find("button.nornis-typography-defaults").Click());
        Assert.That(Sample(cut), Does.Contain("'Newsreader'"));
        cut.Find("button.nornis-typography-save").Click();

        cut.WaitForAssertion(() => Assert.That(_handler.LastPutBody, Is.Not.Null));
        var body = JsonDocument.Parse(_handler.LastPutBody!).RootElement;
        Assert.That(new[] { "font", "size", "lineSpacing" }.Select(p => body.GetProperty(p).ValueKind),
            Is.All.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task ARefusedSave_ShowsTheApisMessage()
    {
        _handler.Refuse = true;
        var cut = RenderCard();
        await Choose(cut, "Lora");

        cut.Find("button.nornis-typography-save").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-typography-error").TextContent, Does.Contain("booklet's fonts")));
        Assert.That(_saved, Is.Null);
    }

    [TestCase(10.3, 0.5, 8, 14, 10.5)]
    [TestCase(20, 0.5, 8, 14, 14)]
    [TestCase(1.33, 0.05, 1, 2, 1.35)]
    [TestCase(0.5, 0.05, 1, 2, 1)]
    public void ATypedValue_SnapsToItsStep_WithinTheBounds(double value, double step, double min, double max, double expected) =>
        Assert.That(TypographyCard.Snap((decimal)value, (decimal)step, (decimal)min, (decimal)max), Is.EqualTo((decimal)expected));

    [Test]
    public void TheWebFonts_LoadEveryFaceButNewsreader()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TypographyCard.WebFontsHref, Does.StartWith("https://fonts.googleapis.com/css2?"));
            Assert.That(TypographyCard.WebFontsHref, Does.Contain("family=EB+Garamond:").And.Contain("family=Source+Serif+4:"));
            Assert.That(TypographyCard.WebFontsHref, Does.Not.Contain("Newsreader"), "the app already loads it");
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public bool Refuse { get; set; }
        public string? LastPutBody { get; private set; }
        public string? LastPutPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastPutPath = request.RequestUri!.AbsolutePath;
            LastPutBody = await request.Content!.ReadAsStringAsync(ct);
            if (Refuse)
            {
                return Json(HttpStatusCode.BadRequest,
                    """{"code":"invalid_typography","message":"That isn't one of the booklet's fonts."}""");
            }

            return Json(HttpStatusCode.OK, $$"""
                {"id":"{{AdventureId}}","worldId":"{{WorldId}}","campaignId":null,"title":"The Silver Key",
                 "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                 "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-03T10:00:00+00:00","sections":[],
                 "typography":{"font":"EbGaramond","size":11.5,"lineSpacing":null} }
                """);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
