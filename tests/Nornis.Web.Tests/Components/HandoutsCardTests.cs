using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The Handouts card (feature 38): it lists the booklet's handouts in print order, saves the whole
/// ordered list on every add, move and remove, and its picker offers only printable maps and
/// handouts, this adventure's first.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class HandoutsCardTests : BunitContext
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid AdventureId = Guid.NewGuid();
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly LibraryDocumentDto OwnMap = Doc("Vault map", "Map", "image/png", AdventureId);
    private static readonly LibraryDocumentDto OtherHandout = Doc("Ransom note", "Handout", "application/pdf", null);
    private static readonly LibraryDocumentDto Gif = Doc("Animated sign", "Handout", "image/gif", null);
    private static readonly LibraryDocumentDto Book = Doc("Monster book", "Book", "application/pdf", null);
    private static readonly LibraryDocumentDto Pending = Doc("Half uploaded", "Map", "image/png", null, status: "PendingUpload");

    private StubHandler _handler = null!;
    private AdventureDto? _saved;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler { Library = [OwnMap, OtherHandout, Gif, Book, Pending] };
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

    private static LibraryDocumentDto Doc(string title, string category, string contentType, Guid? linkedTo, string status = "Ready") => new(
        Guid.NewGuid(), WorldId, title, title + ".file", contentType, 8, category == "Book" ? "Sourcebook" : category,
        "GMOnly", status, null, 0, null, Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
        title.ToLowerInvariant().Replace(' ', '-'), Category: category, LinkedAdventureId: linkedTo);

    private static AdventureHandoutDto Handout(LibraryDocumentDto doc) =>
        new(doc.Id, doc.Title, doc.Slug, doc.Category, doc.ContentType, doc.Visibility);

    private static AdventureDto Adventure(params LibraryDocumentDto[] handouts) => new(
        AdventureId, WorldId, null, "The Silver Key", "the-silver-key", null, null,
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, [], Handouts: handouts.Select(Handout).ToList());

    private IRenderedComponent<HandoutsCard> RenderCard(AdventureDto adventure)
    {
        Render<MudBlazor.MudPopoverProvider>();
        return Render<HandoutsCard>(p => p
            .Add(x => x.Adventure, adventure)
            .Add(x => x.OnSaved, (AdventureDto a) => _saved = a));
    }

    [Test]
    public void WithNoHandouts_TheCardSaysTheBookletPrintsThemAtItsBack()
    {
        var cut = RenderCard(Adventure());

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-handouts-empty").TextContent, Does.Contain("prints them at its back"));
            Assert.That(cut.FindAll(".nornis-handout"), Is.Empty);
        });
    }

    [Test]
    public void TheHandouts_ListInPrintOrder_AndSayWhoCanSeeThem()
    {
        var cut = RenderCard(Adventure(OtherHandout, OwnMap));

        var rows = cut.FindAll(".nornis-handout");
        Assert.Multiple(() =>
        {
            Assert.That(rows.Select(r => r.QuerySelector(".nornis-list-title")!.TextContent), Is.EqualTo(["Ransom note", "Vault map"]));
            Assert.That(rows[0].TextContent, Does.Contain("Handout · PDF · GM only"));
            Assert.That(rows[1].TextContent, Does.Contain("Map · image"));
            Assert.That(cut.Find(".nornis-handouts-caption").TextContent, Does.Contain("doesn't change who can see it"));
            Assert.That(cut.FindAll(".nornis-handout-up")[0].HasAttribute("disabled"), Is.True, "the first can't move up");
            Assert.That(cut.FindAll(".nornis-handout-down")[1].HasAttribute("disabled"), Is.True, "the last can't move down");
        });
    }

    [Test]
    public void ThePicker_OffersPrintableMapsAndHandouts_ThisAdventuresFirst_LeavingOutTheChosen()
    {
        var cut = RenderCard(Adventure());

        cut.Find(".nornis-handouts-add").Click();

        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-handout-choice"), Has.Count.EqualTo(2)));
        var titles = cut.FindAll(".nornis-handout-choice .nornis-list-title").Select(t => t.TextContent);
        Assert.Multiple(() =>
        {
            Assert.That(titles, Is.EqualTo(["Vault map", "Ransom note"]),
                "this adventure's map first; the GIF, the book and the unfinished upload can't be printed");
            Assert.That(cut.Find(".nornis-handouts-picker").TextContent, Does.Contain("This adventure's"));
            Assert.That(cut.Find(".nornis-handouts-picker").TextContent, Does.Contain("Elsewhere in the Library"));
        });
    }

    [Test]
    public void ThePickersChips_NarrowItToMaps_AndTheSearchByTitle()
    {
        var cut = RenderCard(Adventure());
        cut.Find(".nornis-handouts-add").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-handout-choice"), Has.Count.EqualTo(2)));

        cut.FindAll(".nornis-handouts-chips .mud-chip").Single(c => c.TextContent.StartsWith("Maps", StringComparison.Ordinal)).Click();
        Assert.That(cut.FindAll(".nornis-handout-choice .nornis-list-title").Select(t => t.TextContent), Is.EqualTo(["Vault map"]));

        cut.FindAll(".nornis-handouts-chips .mud-chip").First().Click();
        cut.Find(".nornis-handouts-search input").Input("ransom");
        Assert.That(cut.FindAll(".nornis-handout-choice .nornis-list-title").Select(t => t.TextContent), Is.EqualTo(["Ransom note"]));
    }

    [Test]
    public void Adding_SavesTheWholeList_WithTheNewOneLast()
    {
        _handler.Answer = Adventure(OtherHandout, OwnMap);
        var cut = RenderCard(Adventure(OtherHandout));
        cut.Find(".nornis-handouts-add").Click();
        cut.WaitForAssertion(() => Assert.That(cut.FindAll(".nornis-handout-choice"), Has.Count.EqualTo(1)));

        cut.Find(".nornis-handout-choose").Click();

        cut.WaitForAssertion(() => Assert.That(_saved, Is.Not.Null));
        Assert.Multiple(() =>
        {
            Assert.That(_handler.Last, Is.EqualTo($"PUT /api/worlds/{WorldId}/prep/{AdventureId}/handouts"));
            Assert.That(Ids(_handler.LastBody), Is.EqualTo([OtherHandout.Id, OwnMap.Id]));
        });
    }

    [Test]
    public void MovingAndRemoving_SaveTheReorderedList()
    {
        _handler.Answer = Adventure(OwnMap, OtherHandout);
        var cut = RenderCard(Adventure(OtherHandout, OwnMap));

        cut.FindAll(".nornis-handout-down")[0].Click();
        cut.WaitForAssertion(() => Assert.That(Ids(_handler.LastBody), Is.EqualTo([OwnMap.Id, OtherHandout.Id])));

        cut.FindAll(".nornis-handout-remove")[1].Click();
        cut.WaitForAssertion(() => Assert.That(Ids(_handler.LastBody), Is.EqualTo([OtherHandout.Id])));
    }

    [Test]
    public void ARefusal_IsShown_AndTheListStaysAsItWas()
    {
        _handler.Refusal = """{"code":"not_a_handout","message":"\"Ransom note\" can't be printed as a handout."}""";
        var cut = RenderCard(Adventure(OtherHandout, OwnMap));

        cut.FindAll(".nornis-handout-remove")[0].Click();

        cut.WaitForAssertion(() => Assert.That(cut.Find(".nornis-handouts-error").TextContent, Does.Contain("can't be printed")));
        Assert.Multiple(() =>
        {
            Assert.That(_saved, Is.Null);
            Assert.That(cut.FindAll(".nornis-handout"), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AtTheLimit_AddAndUploadAreOff_AndTheCardSaysWhy()
    {
        var full = Enumerable.Range(1, HandoutChoices.MaxHandouts).Select(i => Doc($"Map {i}", "Map", "image/png", null)).ToArray();
        var cut = RenderCard(Adventure(full));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Find(".nornis-handouts-add").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find(".nornis-handouts-upload").HasAttribute("disabled"), Is.True);
            Assert.That(cut.Find(".nornis-handouts-full").TextContent, Does.Contain($"at most {HandoutChoices.MaxHandouts}"));
        });
    }

    private static IReadOnlyList<Guid> Ids(string? body) =>
        JsonSerializer.Deserialize<SetHandoutsRequest>(body!, Web)!.DocumentIds;

    private sealed class StubHandler : HttpMessageHandler
    {
        public IReadOnlyList<LibraryDocumentDto> Library { get; set; } = [];
        public AdventureDto? Answer { get; set; }
        public string? Refusal { get; set; }
        public string? Last { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/library", StringComparison.Ordinal))
            {
                return Json(JsonSerializer.Serialize(Library, Web));
            }

            Last = $"{request.Method} {path}";
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (Refusal is not null)
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(Refusal, Encoding.UTF8, "application/json") };
            }

            return Json(JsonSerializer.Serialize(Answer ?? Adventure(), Web));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
    }
}
