using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Shared;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The review queue's filter bar: present on the Review tab, absent when the panel is hosting
/// one note for the import walk, and offering only the types and visibilities that are
/// actually in the queue. The narrowing itself is <c>ReviewQueueFilterTests</c>' job.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class ReviewQueuePanelTests : BunitContext
{
    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task SelectWorldAsync()
    {
        var worlds = Services.GetRequiredService<WorldState>();
        await worlds.EnsureSelectionRestoredAsync();
    }

    [Test]
    public async Task OnTheReviewTab_TheBarOffersWhatIsInTheQueue()
    {
        await SelectWorldAsync();

        var cut = Render<ReviewQueuePanel>();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Black Harbor")));
        var selects = cut.FindComponents<MudBlazor.MudSelect<string>>();
        Assert.That(selects, Has.Count.EqualTo(2), "type and visibility");
        Assert.Multiple(() =>
        {
            Assert.That(selects[0].Instance.Label, Is.EqualTo("Type"));
            Assert.That(selects[1].Instance.Label, Is.EqualTo("Visibility"));
            Assert.That(cut.FindComponents<MudBlazor.MudSelectItem<string>>().Select(i => i.Instance.Value),
                Is.EquivalentTo(new List<string> { "AddFact", "CreateArtifact", "PartyVisible", "GMOnly" }),
                "only what the queue holds — no Merge, no Private");
        });
    }

    [Test]
    public async Task HostingOneNote_HasNoBar()
    {
        await SelectWorldAsync();

        var cut = Render<ReviewQueuePanel>(p => p.Add(x => x.SourceIdFilter, _handler.SourceId));

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Black Harbor")));
        Assert.That(cut.FindComponents<MudBlazor.MudSelect<string>>(), Is.Empty);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public Guid SourceId { get; } = Guid.NewGuid();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Task.FromResult(Json($$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                      "gameSystem":null,"myRole":"GM"}]
                    """));
            }

            if (path.EndsWith("/reviews/proposals", StringComparison.Ordinal))
            {
                return Task.FromResult(Json($$"""
                    {"hasMore":false,"proposals":[
                      {"id":"{{Guid.NewGuid()}}","reviewBatchId":"{{Guid.NewGuid()}}","changeType":"AddFact","targetType":"Artifact",
                       "targetId":null,"proposedValueJson":"{\"predicate\":\"location\",\"value\":\"Black Harbor\",\"visibility\":\"PartyVisible\"}",
                       "rationale":null,"confidence":0.9,"status":"Pending","createdAt":"2026-09-10T00:00:00+00:00",
                       "sourceId":"{{SourceId}}","sourceTitle":"Session 1"},
                      {"id":"{{Guid.NewGuid()}}","reviewBatchId":"{{Guid.NewGuid()}}","changeType":"CreateArtifact","targetType":"Artifact",
                       "targetId":null,"proposedValueJson":"{\"name\":\"Captain Voss\",\"type\":\"Character\",\"visibility\":\"GMOnly\"}",
                       "rationale":null,"confidence":0.8,"status":"Pending","createdAt":"2026-09-10T00:00:00+00:00",
                       "sourceId":"{{SourceId}}","sourceTitle":"Session 1"}
                    ]}
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
