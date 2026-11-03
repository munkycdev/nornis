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
/// The Book details card (feature 32): seven fields in Bibliotaph's order, a pencil on only the
/// four the GM edits, and a save that sends all four with only the edited one changed.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class BookDetailsCardTests : BunitContext
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

    private static AdventureDto Adventure(AdventureDetailsDto details, PrintDetailsDto print) =>
        new(AdventureId, WorldId, null, "The Silver Key", "the-silver-key", "A heist.", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], details, print);

    private static readonly AdventureDetailsDto Stored = new(null, "1st", null, 2026);

    private static readonly PrintDetailsDto Preview =
        new("The Silver Key", "Fate Accelerated", "1st", "Nornis adventure", null, "Nornis Press", "kelda", 2026);

    private IRenderedComponent<BookDetailsCard> RenderCard(AdventureDto? adventure = null)
    {
        Render<MudPopoverProvider>();
        return Render<BookDetailsCard>(p => p
            .Add(x => x.Adventure, adventure ?? Adventure(Stored, Preview))
            .Add(x => x.OnSaved, (AdventureDto a) => _saved = a));
    }

    private static string Value(IRenderedComponent<BookDetailsCard> cut, string field) =>
        cut.Find($"[data-field='{field}'] .nornis-book-details-value").TextContent;

    [Test]
    public void TheRows_AreBibliotaphsFields_InItsOrder()
    {
        var cut = RenderCard();

        Assert.That(cut.FindAll(".nornis-book-details-label").Select(l => l.TextContent),
            Is.EqualTo((string[])["Title", "Game system", "Edition", "Type", "Levels", "Publisher", "Authors", "Year"]));
    }

    [Test]
    public void OnlyTheFourEditableRows_HaveAPencil()
    {
        var cut = RenderCard();

        var withPencil = cut.FindAll(".nornis-book-details-row")
            .Where(r => r.QuerySelector(".nornis-book-details-pencil") is not null)
            .Select(r => r.GetAttribute("data-field"));
        Assert.That(withPencil, Is.EqualTo((string[])["gameSystem", "edition", "levels", "year"]));
    }

    [Test]
    public void TheCard_SaysNotSet_FromTheWorld_AndSetByNornis()
    {
        var cut = RenderCard();

        Assert.Multiple(() =>
        {
            Assert.That(Value(cut, BookDetailsCard.LevelsField), Does.Contain("Not set"));
            Assert.That(Value(cut, BookDetailsCard.GameSystemField), Does.Contain("Fate Accelerated").And.Contain("From the world"));
            Assert.That(Value(cut, BookDetailsCard.TypeField), Does.Contain("Nornis adventure").And.Contain("Set by Nornis"));
            Assert.That(Value(cut, BookDetailsCard.PublisherField), Does.Contain("Nornis Press").And.Contain("Set by Nornis"));
            Assert.That(Value(cut, BookDetailsCard.AuthorsField), Does.Contain("kelda").And.Contain("Set by Nornis"));
            Assert.That(Value(cut, BookDetailsCard.YearField), Does.Contain("2026"));
        });
    }

    [Test]
    public void AGameSystemOfItsOwn_IsNotMarkedAsTheWorlds()
    {
        var cut = RenderCard(Adventure(Stored with { GameSystem = "Shadowdark" }, Preview with { GameSystem = "Shadowdark" }));

        Assert.That(Value(cut, BookDetailsCard.GameSystemField), Does.Not.Contain("From the world"));
    }

    [Test]
    public void SavingOneField_SendsAllFour_WithOnlyThatOneChanged()
    {
        var cut = RenderCard();

        cut.Find("[data-field='levels'] .nornis-book-details-pencil").Click();
        cut.Find("[data-field='levels'] input").Input("1–3");
        cut.Find("[data-field='levels'] .nornis-book-details-save").Click();

        cut.WaitForAssertion(() => Assert.That(_saved, Is.Not.Null));
        var body = JsonDocument.Parse(_handler.LastPutBody!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastPutPath, Is.EqualTo($"/api/worlds/{WorldId}/prep/{AdventureId}/details"));
            Assert.That(body.GetProperty("gameSystem").ValueKind, Is.EqualTo(JsonValueKind.Null), "the stored value, not the world's");
            Assert.That(body.GetProperty("edition").GetString(), Is.EqualTo("1st"));
            Assert.That(body.GetProperty("levels").GetString(), Is.EqualTo("1–3"));
            Assert.That(body.GetProperty("year").GetInt32(), Is.EqualTo(2026));
        });
    }

    [Test]
    public void ClearingAField_SendsNull()
    {
        var cut = RenderCard();

        cut.Find("[data-field='year'] .nornis-book-details-pencil").Click();
        cut.Find("[data-field='year'] input").Input("");
        cut.Find("[data-field='year'] .nornis-book-details-save").Click();

        cut.WaitForAssertion(() => Assert.That(_handler.LastPutBody, Is.Not.Null));
        Assert.That(JsonDocument.Parse(_handler.LastPutBody!).RootElement.GetProperty("year").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public void Enter_Saves_AndEscape_Cancels()
    {
        var cut = RenderCard();

        cut.Find("[data-field='edition'] .nornis-book-details-pencil").Click();
        cut.Find("[data-field='edition'] input").Input("2nd");
        cut.Find("[data-field='edition'] .nornis-book-details-edit").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.That(cut.FindAll("[data-field='edition'] input"), Is.Empty);
        Assert.That(_handler.LastPutBody, Is.Null);

        cut.Find("[data-field='edition'] .nornis-book-details-pencil").Click();
        cut.Find("[data-field='edition'] input").Input("2nd");
        cut.Find("[data-field='edition'] .nornis-book-details-edit").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.WaitForAssertion(() => Assert.That(_handler.LastPutBody, Does.Contain("\"edition\":\"2nd\"")));
    }

    [Test]
    public void AYearThatIsNotANumber_IsRefusedHere_AndNothingIsSent()
    {
        var cut = RenderCard();

        cut.Find("[data-field='year'] .nornis-book-details-pencil").Click();
        cut.Find("[data-field='year'] input").Input("soon");
        cut.Find("[data-field='year'] .nornis-book-details-save").Click();

        Assert.That(cut.Find(".nornis-book-details-error").TextContent, Does.Contain("must be a number"));
        Assert.That(_handler.LastPutBody, Is.Null);
    }

    [Test]
    public void ARefusal_ShowsTheApisMessage_AndKeepsTheFieldOpen()
    {
        _handler.Refuse = true;
        var cut = RenderCard();

        cut.Find("[data-field='year'] .nornis-book-details-pencil").Click();
        cut.Find("[data-field='year'] input").Input("1066");
        cut.Find("[data-field='year'] .nornis-book-details-save").Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-book-details-error").TextContent, Does.Contain("The year must be between")));
        Assert.That(cut.FindAll("[data-field='year'] input"), Has.Count.EqualTo(1));
        Assert.That(_saved, Is.Null);
    }

    [Test]
    public void WithoutAPreview_TheCardIsNotShown()
    {
        var cut = RenderCard(Adventure(Stored, Preview) with { PrintDetails = null });

        Assert.That(cut.FindAll(".nornis-book-details"), Is.Empty);
    }

    // ------------------------------------------------------------------ AI use (feature 38) --

    private static AdventureDto WithAi(AdventureAiUseDto ai) => Adventure(Stored, Preview) with { AiUse = ai };

    [Test]
    public void TheAiBox_IsTheGmsToTick_WhileNothingIsRecorded()
    {
        var cut = RenderCard(WithAi(new(false, false, false, false, "None")));

        var box = cut.Find(".nornis-book-details-ai input[type=checkbox]");
        Assert.Multiple(() =>
        {
            Assert.That(box.HasAttribute("disabled"), Is.False);
            Assert.That(box.HasAttribute("checked"), Is.False);
            Assert.That(cut.Find(".nornis-book-details-ai").TextContent, Does.Contain("AI was used to make this adventure"));
            Assert.That(cut.FindAll(".nornis-book-details-ai-reason"), Is.Empty);
        });
    }

    [Test]
    public void TickingTheAiBox_SendsTheGmsTick_AndRaisesTheSavedAdventure()
    {
        var cut = RenderCard(WithAi(new(false, false, false, false, "None")));

        cut.Find(".nornis-book-details-ai input[type=checkbox]").Change(true);

        Assert.Multiple(() =>
        {
            Assert.That(_handler.LastPutPath, Is.EqualTo($"/api/worlds/{WorldId}/prep/{AdventureId}/ai-declared"));
            Assert.That(JsonDocument.Parse(_handler.LastPutBody!).RootElement.GetProperty("declared").GetBoolean(), Is.True);
            Assert.That(_saved, Is.Not.Null);
        });
    }

    [TestCase(true, false, "The Loremaster drafted text in this adventure.")]
    [TestCase(false, true, "The cover art was generated with AI.")]
    [TestCase(true, true, "The Loremaster drafted text and the cover art was generated in this adventure.")]
    public void RecordedUse_TicksAndLocksTheBox_AndSaysWhy(bool wroteText, bool madeArt, string reason)
    {
        var cut = RenderCard(WithAi(new(wroteText, madeArt, false, true, "Text")));

        var box = cut.Find(".nornis-book-details-ai input[type=checkbox]");
        Assert.Multiple(() =>
        {
            Assert.That(box.HasAttribute("disabled"), Is.True);
            Assert.That(box.HasAttribute("checked"), Is.True, "recorded use ticks it");
            Assert.That(cut.Find(".nornis-book-details-ai-reason").TextContent, Is.EqualTo(reason));
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
                    """{"code":"invalid_details","message":"The year must be between 1900 and 5 years from now."}""");
            }

            return Json(HttpStatusCode.OK, $$"""
                {"id":"{{AdventureId}}","worldId":"{{WorldId}}","campaignId":null,"title":"The Silver Key",
                 "slug":"the-silver-key","pitch":"A heist.","lastPrintedDocumentId":null,
                 "createdAt":"2026-11-02T09:00:00+00:00","updatedAt":"2026-11-03T10:00:00+00:00","sections":[],
                 "details":{"gameSystem":null,"edition":"1st","levels":"1–3","year":2026},
                 "printDetails":{"title":"The Silver Key","gameSystem":"Fate Accelerated","edition":"1st",
                   "type":"Nornis adventure","levels":"1–3","publisher":"Nornis Press","author":"kelda","year":2026} }
                """);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
