using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The head of an adventure's page (3.18): the title and pitch open into a box where they are
/// shown, the campaign saves when picked, and every save sends all three as the API takes them.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class AdventureHeadingTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid AdventureId = Guid.NewGuid();
    private static readonly Guid CampaignId = Guid.NewGuid();

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

    private IRenderedComponent<AdventureHeading> RenderHeading(string? pitch = "A heist.")
    {
        Render<MudPopoverProvider>();
        var adventure = new AdventureDto(AdventureId, WorldId, null, "The Silver Key", "the-silver-key", pitch, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        var campaigns = new[]
        {
            new CampaignDto(CampaignId, WorldId, "Black Harbor", null, "Active", null, null,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid()),
        };
        return Render<AdventureHeading>(p => p
            .Add(x => x.WorldId, WorldId)
            .Add(x => x.Adventure, adventure)
            .Add(x => x.Campaigns, campaigns)
            .Add(x => x.OnSaved, (AdventureDto a) => _saved = a));
    }

    private JsonElement Sent => JsonDocument.Parse(_handler.LastBody!).RootElement;

    [Test]
    public void ThePitch_ShowsAPrompt_WhenThereIsNone()
    {
        var cut = RenderHeading(pitch: null);

        Assert.That(cut.Find(".nornis-prep-heading-pitch .nornis-prep-heading-none").TextContent, Is.EqualTo("Add a pitch"));
    }

    [Test]
    public async Task Enter_SavesTheTitle_WithThePitchAndCampaignAsTheyAre()
    {
        var cut = RenderHeading();

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-heading-title").Click());
        cut.Find(".nornis-prep-heading-title-input input").Input("  The Golden Key ");
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-heading-edit").KeyDown(new KeyboardEventArgs { Key = "Enter" }));

        cut.WaitForAssertion(() => Assert.That(_saved, Is.Not.Null));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastPath, Is.EqualTo($"/api/worlds/{WorldId}/prep/{AdventureId}"));
            Assert.That(Sent.GetProperty("title").GetString(), Is.EqualTo("The Golden Key"), "trimmed");
            Assert.That(Sent.GetProperty("pitch").GetString(), Is.EqualTo("A heist."));
            Assert.That(Sent.GetProperty("campaignId").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(cut.FindAll(".nornis-prep-heading-edit"), Is.Empty, "the box closes on a save");
        });
    }

    [Test]
    public async Task Enter_IsANewLineInThePitch_AndCtrlEnterSavesIt()
    {
        var cut = RenderHeading();

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-heading-pitch").Click());
        cut.Find(".nornis-prep-heading-pitch-input textarea").Input("The key is a lie.");
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-heading-edit").KeyDown(new KeyboardEventArgs { Key = "Enter" }));
        Assert.That(_handler.LastBody, Is.Null, "Enter alone doesn't save the pitch");

        await cut.InvokeAsync(() => cut.Find(".nornis-prep-heading-edit").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true }));

        cut.WaitForAssertion(() => Assert.That(_saved, Is.Not.Null));
        Assert.Multiple(() =>
        {
            Assert.That(Sent.GetProperty("title").GetString(), Is.EqualTo("The Silver Key"));
            Assert.That(Sent.GetProperty("pitch").GetString(), Is.EqualTo("The key is a lie."));
        });
    }

    [Test]
    public async Task Escape_PutsTheTitleBack_WithoutSaving()
    {
        var cut = RenderHeading();

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-heading-title").Click());
        cut.Find(".nornis-prep-heading-title-input input").Input("Something else");
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-heading-edit").KeyDown(new KeyboardEventArgs { Key = "Escape" }));

        Assert.Multiple(() =>
        {
            Assert.That(cut.FindAll(".nornis-prep-heading-edit"), Is.Empty);
            Assert.That(cut.Find(".nornis-prep-heading-title").TextContent.Trim(), Is.EqualTo("The Silver Key"));
            Assert.That(_handler.LastBody, Is.Null);
        });
    }

    [Test]
    public async Task ABlankTitle_IsRefused_BeforeTheApi()
    {
        var cut = RenderHeading();

        await cut.InvokeAsync(() => cut.Find("button.nornis-prep-heading-title").Click());
        cut.Find(".nornis-prep-heading-title-input input").Input("   ");
        await cut.InvokeAsync(() => cut.Find(".nornis-prep-heading-edit").KeyDown(new KeyboardEventArgs { Key = "Enter" }));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-prep-heading-error").TextContent, Is.EqualTo("An adventure needs a title."));
            Assert.That(cut.Find(".nornis-prep-heading-save").HasAttribute("disabled"), Is.True);
            Assert.That(_handler.LastBody, Is.Null);
        });
    }

    [Test]
    public async Task PickingACampaign_SavesIt_AndAFailureIsShown()
    {
        var cut = RenderHeading();
        _handler.Refuse = true;

        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<Guid?>>().Instance.ValueChanged.InvokeAsync(CampaignId));

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-prep-heading-error").TextContent, Is.EqualTo("That campaign isn't in this world.")));
        Assert.That(_saved, Is.Null);

        _handler.Refuse = false;
        await cut.InvokeAsync(() => cut.FindComponent<MudSelect<Guid?>>().Instance.ValueChanged.InvokeAsync(CampaignId));

        cut.WaitForAssertion(() => Assert.That(_saved, Is.Not.Null));
        Assert.Multiple(() =>
        {
            Assert.That(Sent.GetProperty("campaignId").GetGuid(), Is.EqualTo(CampaignId));
            Assert.That(cut.FindAll(".nornis-prep-heading-error"), Is.Empty);
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public bool Refuse { get; set; }
        public string? LastBody { get; private set; }
        public string? LastPath { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastPath = request.RequestUri!.AbsolutePath;
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            if (Refuse)
            {
                return Json(HttpStatusCode.BadRequest, """{"code":"invalid_campaign","message":"That campaign isn't in this world."}""");
            }

            using var sent = JsonDocument.Parse(LastBody);
            return Json(HttpStatusCode.OK, $$"""
                {"id":"{{AdventureId}}","worldId":"{{WorldId}}","campaignId":{{sent.RootElement.GetProperty("campaignId").GetRawText()}},
                 "title":{{sent.RootElement.GetProperty("title").GetRawText()}},"slug":"the-silver-key",
                 "pitch":{{sent.RootElement.GetProperty("pitch").GetRawText()}},"lastPrintedDocumentId":null,
                 "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-03T10:00:00+00:00","sections":[] }
                """);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
