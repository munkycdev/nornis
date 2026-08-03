using System.Net;
using System.Text;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// World Memory shows its working — the severity table, each severity's subtotal, the cap.
/// It used to compute all of that here, from its own copy of the rule that lives in
/// ContinuityAuditService. Nothing spans the two deployables, so the copies could drift and
/// the page would confidently render a total that disagreed with the score beside it.
/// These tests hand it a breakdown no client could have derived and check it renders that,
/// which is what makes the page a renderer rather than a second implementation.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class WorldMemoryPenaltyTests : BunitContext
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
        Renderer.SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    /// <summary>
    /// Deliberately not the real weights. If the page still owned the rule it would render 12/6/2
    /// and these numbers would never appear.
    /// </summary>
    private static ContinuityPenaltyBreakdown Breakdown(int raw, int capped, int cap, bool isCapped) =>
        new(
            Lines: [new ContinuityPenaltyLine("High", 99, 2, 198)],
            Scale: [new ContinuitySeverityWeight("High", 99), new ContinuitySeverityWeight("Low", 7)],
            StaleSuspendedCount: 3,
            RawPenalty: raw,
            CappedPenalty: capped,
            Cap: cap,
            IsCapped: isCapped);

    private async Task<string> RenderWithAsync(ContinuityPenaltyBreakdown penalty)
    {
        var worlds = Services.GetRequiredService<WorldState>();
        await worlds.EnsureSelectionRestoredAsync();

        var cut = Render<WorldMemory>();
        await cut.InvokeAsync(() => worlds.SetContinuity(new ContinuityAssessment(
            HasData: true,
            AssessmentId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Model: "nornis-ask",
            Score: 70,
            EffectiveScore: 61,
            HeuristicScore: 90,
            Findings:
            [
                new ContinuityFinding(Guid.NewGuid(), "Timeline", "High", "Two dates disagree",
                    null, [], [], null, "Open", false),
            ],
            Penalty: penalty)));

        return cut.Markup;
    }

    [Test]
    public async Task ThePageRendersTheServersNumbers_NotItsOwn()
    {
        var markup = await RenderWithAsync(Breakdown(raw: 198, capped: 150, cap: 150, isCapped: true));

        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("2 × 99"), "the per-severity line comes from the server");
            Assert.That(markup, Does.Contain("−198"), "so does its subtotal");
            Assert.That(markup, Does.Contain("−150"), "and the capped total");
            Assert.That(markup, Does.Contain("3 suspended"), "and the stale count");
        });
    }

    [Test]
    public async Task TheCapIsRendered_NotAssumedToBeForty()
    {
        var markup = await RenderWithAsync(Breakdown(raw: 198, capped: 150, cap: 150, isCapped: true));

        // The old page hard-coded 40 in two places. A different cap arriving from the server has
        // to show up, or the page is still speaking for the rule instead of from it.
        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Contain("capped at 150"));
            Assert.That(markup, Does.Not.Contain("capped at 40"));
        });
    }

    [Test]
    public async Task TheScaleIsRendered_SoTheProseCannotGoStale()
    {
        var markup = await RenderWithAsync(Breakdown(raw: 198, capped: 150, cap: 150, isCapped: true));

        // The explanatory paragraph states the table as well as applying it; both come from the
        // same place now.
        Assert.That(markup, Does.Contain("High 99, Low 7"));
    }

    [Test]
    public async Task WhenTheCapDidNotBite_NothingClaimsItDid()
    {
        var markup = await RenderWithAsync(Breakdown(raw: 18, capped: 18, cap: 150, isCapped: false));

        // The prose states the cap either way — that is the rule, not a claim about this world.
        // What must not appear is the parenthetical on the score formula or the raw-vs-capped
        // total, both of which assert that the cap actually bit.
        Assert.Multiple(() =>
        {
            Assert.That(markup, Does.Not.Contain("(capped at 150)"));
            Assert.That(markup, Does.Not.Contain("raw 18"));
            Assert.That(markup, Does.Contain("total capped at 150"), "the rule is still stated");
        });
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath == "/api/worlds")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""
                        [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                          "gameSystem":null,"myRole":"GM"}]
                        """,
                        Encoding.UTF8, "application/json"),
                });
            }

            // The assessment is pushed in directly by the test; nothing else the boot touches matters.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
