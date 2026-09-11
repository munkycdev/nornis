using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages.Public.World;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The public world's pages on the entity template (feature 28): what a stranger's page says
/// that the July pages did not — who plays an entry, where an excerpt came from, a campaign's
/// party recap and cast — and what it must not say.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class PublicWorldPagesTests : BunitContext
{
    private static readonly Guid ArtifactId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CampaignId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [SetUp]
    public void SetUp()
    {
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(new StubHandler()) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
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

    [Test]
    public void Entry_SaysWhoPlaysIt_AndHasARailWithoutTheLoremaster()
    {
        var cut = Render<PublicWorldArtifactDetail>(p => p.Add(x => x.Slug, "black-harbor").Add(x => x.Key, ArtifactId.ToString()));

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Played by Henry")));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("nornis-entity-rail"), "the 2.0 template");
            Assert.That(cut.Markup, Does.Contain("Ask this world"), "the rail points at the funded Ask");
            Assert.That(cut.Markup, Does.Not.Contain("nornis-rail-loremaster"), "no conversation panel for strangers");
            Assert.That(cut.Markup, Does.Contain("/w/black-harbor/artifacts/"), "connections link within the public site");
        });
    }

    [Test]
    public void Excerpt_SaysWhereItCameFrom_WithoutALink()
    {
        var cut = Render<PublicWorldSourceDetail>(p => p.Add(x => x.Slug, "black-harbor").Add(x => x.Key, SourceId.ToString()));

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Player&#x27;s Guide").Or.Contain("Player's Guide")));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("pp. 42–45"));
            Assert.That(cut.Markup, Does.Not.Contain("/library/"), "the Library is not public");
            Assert.That(cut.Markup, Does.Contain($"/w/black-harbor/campaigns/{CampaignId}"), "the campaign chip links to the public campaign");
        });
    }

    [Test]
    public void Campaign_ShowsThePartyRecapAndTheCast()
    {
        var cut = Render<PublicWorldCampaignDetail>(p => p.Add(x => x.Slug, "black-harbor").Add(x => x.Key, CampaignId.ToString()));

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("The party has reached Black Harbor")));
        Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Malliano"));
            Assert.That(cut.Markup, Does.Contain("Played by Henry"));
            Assert.That(cut.Markup, Does.Contain("The Missing Caravan"));
            Assert.That(cut.Markup, Does.Not.Contain("Generate recap"), "nothing a GM does");
            Assert.That(cut.Markup, Does.Not.Contain("/characters/"), "no public character page");
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/public/worlds/black-harbor")
            {
                return Task.FromResult(Json("""
                    {"slug":"black-harbor","name":"Black Harbor","description":null,"gameSystem":null,"askEnabled":true}
                    """));
            }

            if (path == $"/api/public/worlds/black-harbor/artifacts/{ArtifactId}")
            {
                return Task.FromResult(Json($$"""
                    {"id":"{{ArtifactId}}","worldId":"{{Guid.NewGuid()}}","type":"Character","name":"Captain Voss","summary":"Harbourmaster.",
                     "status":"Active","visibility":"PartyVisible","confidence":null,"createdAt":"2026-09-10T00:00:00+00:00","updatedAt":"2026-09-10T00:00:00+00:00",
                     "facts":[],"relationships":[{"id":"{{Guid.NewGuid()}}","artifactAId":"{{ArtifactId}}","artifactBId":"{{SourceId}}","type":"AlliedWith","description":null,"confidence":null,"truthState":"Confirmed","visibility":"PartyVisible"}],
                     "connectedArtifacts":[{"id":"{{SourceId}}","name":"Black Harbor","type":"Location","summary":null}],
                     "sourceReferences":[],"playedBy":["Henry"]}
                    """));
            }

            if (path == $"/api/public/worlds/black-harbor/sources/{SourceId}")
            {
                return Task.FromResult(Json($$"""
                    {"id":"{{SourceId}}","worldId":"{{Guid.NewGuid()}}","type":"LibraryExcerpt","title":"Thistlehold — Player's Guide, pp. 42–45",
                     "body":"Excerpt.","uri":null,"occurredAt":null,"createdAt":"2026-09-10T00:00:00+00:00","createdByUserId":"{{Guid.NewGuid()}}",
                     "visibility":"PartyVisible","processingStatus":"Processed","campaignId":"{{CampaignId}}","campaignName":"The Missing Caravan",
                     "extractionEnabled":false,"libraryDocumentId":"{{Guid.NewGuid()}}","libraryDocumentTitle":"Player's Guide","libraryPageFrom":42,"libraryPageTo":45}
                    """));
            }

            if (path == $"/api/public/worlds/black-harbor/sources/{SourceId}/locations")
            {
                return Task.FromResult(Json("[]"));
            }

            if (path == $"/api/public/worlds/black-harbor/campaigns/{CampaignId}/detail")
            {
                return Task.FromResult(Json($$"""
                    {"campaign":{"id":"{{CampaignId}}","worldId":"{{Guid.NewGuid()}}","name":"The Missing Caravan","description":null,"status":"Active",
                                 "startedAt":null,"endedAt":null,"createdAt":"2026-09-10T00:00:00+00:00","updatedAt":"2026-09-10T00:00:00+00:00","createdByUserId":"{{Guid.NewGuid()}}"},
                     "cast":[{"id":"{{Guid.NewGuid()}}","name":"Malliano","playerName":"Henry"}],
                     "artifacts":[],"artifactTotalCount":0,"recentSessions":[],"sessionCount":0,"firstSessionAt":null,"lastSessionAt":null,
                     "recap":{"hasData":true,"generatedAt":"2026-09-10T00:00:00+00:00","content":"The party has reached Black Harbor."}
                    }
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
