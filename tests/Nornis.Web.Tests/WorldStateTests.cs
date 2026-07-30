using System.Net;
using System.Text;
using Nornis.Web.ApiClient;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests;

[TestFixture]
public class WorldStateTests
{
    private static WorldState CreateState() =>
        CreateState(new StubApiHandler());

    private static WorldState CreateState(StubApiHandler handler, string? storedWorldId = null)
    {
        var viewAs = new ViewAsState();
        var client = new NornisApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            viewAs,
            new ActivitySignal(),
            new AuthSessionState());
        return new WorldState(client, viewAs, new FakeJsRuntime { StoredWorldId = storedWorldId });
    }

    [Test]
    public void SetContinuity_ReplacesAssessment_AndRaisesChanged()
    {
        var state = CreateState();
        var changedRaised = false;
        state.Changed += () => changedRaised = true;

        var assessment = new ContinuityAssessment(
            HasData: true,
            AssessmentId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UtcNow,
            Model: "test-model",
            Score: 90,
            EffectiveScore: 84,
            HeuristicScore: 90,
            Findings: []);

        state.SetContinuity(assessment);

        Assert.That(state.Continuity, Is.SameAs(assessment));
        Assert.That(changedRaised, Is.True);
    }

    [Test]
    public void SetContinuity_AcceptsNull_AndRaisesChanged()
    {
        var state = CreateState();
        var changedRaised = false;
        state.Changed += () => changedRaised = true;

        state.SetContinuity(null);

        Assert.That(state.Continuity, Is.Null);
        Assert.That(changedRaised, Is.True);
    }

    [Test]
    public async Task EnsureContinuityLoadedAsync_SharesOneRequestAcrossCallers()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);

        // Sidebar path: world load fetches continuity once.
        await state.EnsureSelectionRestoredAsync();
        // Page paths: Home and World Memory both ensure — neither should refetch.
        await state.EnsureContinuityLoadedAsync();
        await state.EnsureContinuityLoadedAsync();

        Assert.That(handler.AssessmentRequests, Is.EqualTo(1));
        Assert.That(state.Continuity, Is.Not.Null);
        Assert.That(state.Continuity!.EffectiveScore, Is.EqualTo(84));
    }

    [Test]
    public async Task EnsureContinuityLoadedAsync_SkipsGmOnlyEndpointForOtherRoles()
    {
        var handler = new StubApiHandler { MyRole = "Player" };
        var state = CreateState(handler);

        await state.EnsureSelectionRestoredAsync();
        await state.EnsureContinuityLoadedAsync();

        Assert.That(handler.AssessmentRequests, Is.EqualTo(0));
        Assert.That(state.Continuity, Is.Null);
        Assert.That(state.ContinuityError, Is.Null);
    }

    [Test]
    public async Task SetContinuity_MarksCacheSatisfied_SoEnsureDoesNotRefetch()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);
        await state.EnsureSelectionRestoredAsync();

        var fresher = state.Continuity! with { EffectiveScore = 99 };
        state.SetContinuity(fresher);
        await state.EnsureContinuityLoadedAsync();

        Assert.That(handler.AssessmentRequests, Is.EqualTo(1));
        Assert.That(state.Continuity, Is.SameAs(fresher));
    }

    [Test]
    public async Task LoadContinuityAsync_ForcesRefetch()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);
        await state.EnsureSelectionRestoredAsync();

        await state.LoadContinuityAsync();

        Assert.That(handler.AssessmentRequests, Is.EqualTo(2));
    }

    [Test]
    public async Task EnsureContinuityLoadedAsync_ExposesErrorOnFailure()
    {
        var handler = new StubApiHandler { AssessmentStatus = HttpStatusCode.InternalServerError };
        var state = CreateState(handler);

        await state.EnsureSelectionRestoredAsync();

        Assert.That(state.Continuity, Is.Null);
        Assert.That(state.ContinuityError, Is.Not.Null);
    }

    [Test]
    public async Task SetViewingAsPlayer_TogglesEffectiveRole_AndRaisesChanged()
    {
        var state = CreateState();
        await state.EnsureSelectionRestoredAsync();
        Assert.That(state.EffectiveRole, Is.EqualTo("GM"));

        var changedCount = 0;
        state.Changed += () => changedCount++;

        state.SetViewingAsPlayer(true);

        Assert.That(state.ViewingAsPlayer, Is.True);
        Assert.That(state.EffectiveRole, Is.EqualTo("Player"));
        Assert.That(changedCount, Is.EqualTo(1));

        // Idempotent: setting the same value again must not re-notify.
        state.SetViewingAsPlayer(true);
        Assert.That(changedCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ViewingAsPlayer_StampsViewAsHeaderOnApiRequests()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);

        await state.EnsureSelectionRestoredAsync();
        Assert.That(handler.LastWorldsRequestHadViewAs, Is.False);

        state.SetViewingAsPlayer(true);
        await state.ReloadAsync();

        Assert.That(handler.LastWorldsRequestHadViewAs, Is.True);
    }

    [Test]
    public async Task ViewingAsPlayer_SkipsGmOnlyContinuityFetch_AndRestoresOnExit()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);
        await state.EnsureSelectionRestoredAsync();
        Assert.That(handler.AssessmentRequests, Is.EqualTo(1));

        // While viewing as player the GM-only endpoint would 403 — a forced load must skip it.
        state.SetViewingAsPlayer(true);
        await state.LoadContinuityAsync();
        Assert.That(handler.AssessmentRequests, Is.EqualTo(1));
        Assert.That(state.Continuity, Is.Null);

        // Leaving the view refetches what the skip cleared.
        state.SetViewingAsPlayer(false);
        await state.EnsureContinuityLoadedAsync();
        Assert.That(handler.AssessmentRequests, Is.EqualTo(2));
        Assert.That(state.Continuity, Is.Not.Null);
    }

    [Test]
    public async Task Select_DifferentWorld_ExitsPlayerView()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);
        await state.EnsureSelectionRestoredAsync();

        state.SetViewingAsPlayer(true);
        state.Select(handler.SecondWorldId);

        Assert.That(state.ViewingAsPlayer, Is.False);
        Assert.That(state.EffectiveRole, Is.EqualTo("GM"));
    }

    // ------------------------------------------------------- boot / restore --

    [Test]
    public async Task EnsureLoadedAsync_DoesNotSelectAWorld_BeforeTheRestore()
    {
        // An eager first-world pick here is exactly the startup flash: pages would render
        // and fetch for it, then swap to the saved world once localStorage is read.
        var state = CreateState(new StubApiHandler());

        await state.EnsureLoadedAsync();

        Assert.That(state.Worlds, Has.Count.EqualTo(2));
        Assert.That(state.Current, Is.Null);
        Assert.That(state.Ready, Is.False);
    }

    [Test]
    public async Task EnsureSelectionRestoredAsync_PicksTheSavedWorld()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler, storedWorldId: handler.SecondWorldId.ToString());

        await state.EnsureSelectionRestoredAsync();

        Assert.That(state.Ready, Is.True);
        Assert.That(state.Current!.Id, Is.EqualTo(handler.SecondWorldId));
    }

    [Test]
    public async Task EnsureSelectionRestoredAsync_FallsBackToFirst_WhenNothingSaved()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler);

        await state.EnsureSelectionRestoredAsync();

        Assert.That(state.Ready, Is.True);
        Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId));
    }

    [Test]
    public async Task EnsureSelectionRestoredAsync_FallsBackToFirst_WhenSavedWorldIsGone()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler, storedWorldId: Guid.NewGuid().ToString());

        await state.EnsureSelectionRestoredAsync();

        Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId));
    }

    [Test]
    public async Task EnsureSelectionRestoredAsync_IsIdempotent()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler, storedWorldId: handler.SecondWorldId.ToString());
        await state.EnsureSelectionRestoredAsync();

        // A second entry point (another layout render) must not refetch or reselect.
        state.Select(handler.WorldId);
        await state.EnsureSelectionRestoredAsync();

        Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId));
    }

    /// <summary>Serves a two-world list and a canned assessment, counting requests per endpoint.</summary>
    private sealed class StubApiHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public Guid SecondWorldId { get; } = Guid.NewGuid();
        public string MyRole { get; init; } = "GM";
        public HttpStatusCode AssessmentStatus { get; init; } = HttpStatusCode.OK;
        public int AssessmentRequests { get; private set; }
        public bool? LastWorldsRequestHadViewAs { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                LastWorldsRequestHadViewAs = request.Headers.Contains("X-Nornis-View-As");
                return Task.FromResult(Json(
                    $$"""
                    [{"id":"{{WorldId}}","name":"W","description":null,"gameSystem":null,"myRole":"{{MyRole}}"},
                     {"id":"{{SecondWorldId}}","name":"W2","description":null,"gameSystem":null,"myRole":"{{MyRole}}"}]
                    """));
            }

            if (path == $"/api/worlds/{WorldId}/health/assessment")
            {
                AssessmentRequests++;
                if (AssessmentStatus != HttpStatusCode.OK)
                {
                    return Task.FromResult(new HttpResponseMessage(AssessmentStatus));
                }

                return Task.FromResult(Json(
                    $$"""
                    {"hasData":true,"assessmentId":"{{Guid.NewGuid()}}","createdAt":"2026-07-21T00:00:00Z",
                     "model":"test-model","score":90,"effectiveScore":84,"heuristicScore":90,"findings":[]}
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    /// <summary>Answers the localStorage.getItem call WorldState's restore makes.</summary>
    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public string? StoredWorldId { get; init; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            ValueTask.FromResult((TValue)(object?)StoredWorldId!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            ValueTask.FromResult((TValue)(object?)StoredWorldId!);
    }
}
