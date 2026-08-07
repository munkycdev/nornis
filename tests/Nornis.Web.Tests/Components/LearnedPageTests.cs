using System.Net;
using System.Text;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The page's one load-bearing piece of copy. Requirement 2.2 says a reader must not be able to
/// tell a world with nothing left to disclose from one full of secrets — and an empty state
/// reading "nothing left" would give that away in the friendliest possible voice. Pinned here
/// because copy is precisely what a later editor improves.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class LearnedPageTests : BunitContext
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
    public async Task AnEmptyState_SaysNothingNew_AndNeverNothingLeft()
    {
        _handler.HasEntries = false;
        await SelectWorldAsync();

        var cut = Render<Learned>();

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("Nothing new"));
            Assert.That(cut.Markup, Does.Not.Contain("nothing left").IgnoreCase);
            Assert.That(cut.Markup, Does.Not.Contain("no secrets").IgnoreCase);
            Assert.That(cut.Markup, Does.Not.Contain("all caught up on everything").IgnoreCase);
        }));
    }

    [Test]
    public async Task ADisclosure_RendersTheGmsNoteAndTheElement()
    {
        _handler.HasEntries = true;
        await SelectWorldAsync();

        var cut = Render<Learned>();

        cut.WaitForAssertion(() => Assert.Multiple(() =>
        {
            Assert.That(cut.Markup, Does.Contain("The letter you found names the harbourmaster."));
            Assert.That(cut.Markup, Does.Contain("Captain Voss"));
            Assert.That(cut.Markup, Does.Contain("Mark as read"));
            Assert.That(cut.Markup, Does.Contain("Your GM told you"),
                "a disclosure and the record catching up are different events");
        }));
    }

    [Test]
    public async Task TheMarkAsReadAffordance_IsAbsentWhenThereIsNothingToRead()
    {
        _handler.HasEntries = false;
        await SelectWorldAsync();

        var cut = Render<Learned>();

        cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("Nothing new")));
        Assert.That(cut.Markup, Does.Not.Contain("Mark as read"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public bool HasEntries { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Task.FromResult(Json($$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                      "gameSystem":null,"myRole":"Player"}]
                    """));
            }

            if (path.EndsWith("/learned", StringComparison.Ordinal))
            {
                var entries = HasEntries
                    ? $$"""
                        [{"kind":"Disclosed","sourceId":"{{Guid.NewGuid()}}","occurredAt":"2026-08-05T00:00:00+00:00",
                          "gmNote":"The letter you found names the harbourmaster.",
                          "elements":[{"id":"{{Guid.NewGuid()}}","kind":"Artifact","name":"Captain Voss","detail":null}]}]
                        """
                    : "[]";

                return Task.FromResult(Json($$"""
                    {"worldId":"{{WorldId}}","generatedAt":"2026-08-06T00:00:00+00:00",
                     "seenThrough":null,"hasMore":false,"entries":{{entries}}}
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
