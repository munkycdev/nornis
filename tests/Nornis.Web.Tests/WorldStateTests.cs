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

    private static WorldState CreateState(HttpMessageHandler handler, string? storedWorldId = null) =>
        CreateState(handler, out _, storedWorldId);

    private static WorldState CreateState(
        HttpMessageHandler handler, out FakeJsRuntime js, string? storedWorldId = null)
    {
        var viewAs = new ViewAsState();
        var client = new NornisApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            viewAs,
            new ActivitySignal(),
            new AuthSessionState());
        js = new FakeJsRuntime { StoredWorldId = storedWorldId };
        return new WorldState(client, viewAs, js);
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
            Findings: [],
            Penalty: ContinuityPenaltyBreakdown.Empty);

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

    [Category("Authorization")]
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

    [Category("Authorization")]
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
        await state.SelectAsync(handler.SecondWorldId);

        Assert.That(state.ViewingAsPlayer, Is.False);
        Assert.That(state.EffectiveRole, Is.EqualTo("GM"));
    }

    #region Selecting remembers

    [Test]
    public async Task SelectAsync_RemembersTheChoiceForTheNextLoad()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler, out var js);
        await state.EnsureSelectionRestoredAsync();

        await state.SelectAsync(handler.SecondWorldId);

        // Persisting used to live in the nav menu's own wrapper, so the three callers that were
        // not the nav menu — accepting an invite, creating a first world, finishing onboarding —
        // selected a world for the session and handed back the old one on the next full load.
        Assert.That(js.LastWriteTo(WorldState.StorageKey), Is.EqualTo(handler.SecondWorldId.ToString()));
    }

    [Test]
    public async Task SelectAsync_ReSelectingTheCurrentWorld_StillWritesIt()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler, out var js);
        await state.EnsureSelectionRestoredAsync();
        Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId), "starts on the first world");

        await state.SelectAsync(handler.WorldId);

        // Re-selecting is how a caller repairs a saved value that is missing or stale — which is
        // exactly the state a first-ever world lands in. Returning early on "no change" would
        // make that repair impossible.
        Assert.That(js.LastWriteTo(WorldState.StorageKey), Is.EqualTo(handler.WorldId.ToString()));
    }

    [Test]
    public async Task SelectAsync_AWorldTheUserIsNotIn_ChangesAndRemembersNothing()
    {
        var handler = new StubApiHandler();
        var state = CreateState(handler, out var js);
        await state.EnsureSelectionRestoredAsync();

        await state.SelectAsync(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId));
            Assert.That(js.LastWriteTo(WorldState.StorageKey), Is.Null);
        });
    }

    #endregion

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
        await state.SelectAsync(handler.WorldId);
        await state.EnsureSelectionRestoredAsync();

        Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId));
    }

    [Test]
    public async Task LoadContinuity_ForAWorldTheUserHasLeft_IsDiscarded()
    {
        // The continuity score is a number a GM reads and acts on, so painting the previous
        // world's under the new world's name is worse than showing nothing. A slow assessment
        // plus a world switch is all it takes, and both are ordinary.
        var handler = new GatedAssessmentHandler();
        var state = CreateState(handler);
        await state.EnsureSelectionRestoredAsync();
        Assert.That(state.Current!.Id, Is.EqualTo(handler.WorldId), "starts on the first world");

        // Arm only now: the restore above loads continuity too, and gating that would
        // deadlock the setup rather than test anything.
        handler.Arm();
        var inFlight = state.LoadContinuityAsync();
        await handler.RequestReceived.Task;

        // Switch away while the first world's assessment is still in flight. Select clears
        // the score and starts a load for the new world, which this handler 404s.
        await state.SelectAsync(handler.SecondWorldId);

        handler.Release();
        await inFlight;

        Assert.That(state.Continuity, Is.Null,
            "the departed world's assessment must not land under the world now on screen");
    }

    /// <summary>
    /// Holds the first world's assessment open until released, so a world switch can be
    /// interleaved with a response that is genuinely still in flight.
    /// </summary>
    private sealed class GatedAssessmentHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _armed;

        public Guid WorldId { get; } = Guid.NewGuid();
        public Guid SecondWorldId { get; } = Guid.NewGuid();
        public TaskCompletionSource RequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Makes the next assessment request block until <see cref="Release"/>.</summary>
        public void Arm() => _armed = true;

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Json(
                    $$"""
                    [{"id":"{{WorldId}}","name":"W","description":null,"gameSystem":null,"myRole":"GM"},
                     {"id":"{{SecondWorldId}}","name":"W2","description":null,"gameSystem":null,"myRole":"GM"}]
                    """);
            }

            if (path == $"/api/worlds/{WorldId}/health/assessment")
            {
                if (_armed)
                {
                    RequestReceived.TrySetResult();
                    await _gate.Task;
                }

                return Json(
                    $$"""
                    {"hasData":true,"assessmentId":"{{Guid.NewGuid()}}","createdAt":"2026-07-21T00:00:00Z",
                     "model":"test-model","score":90,"effectiveScore":84,"heuristicScore":90,"findings":[]}
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
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
    /// <summary>
    /// Stands in for localStorage. Writes are recorded rather than ignored: the previous version
    /// cast every return to the stored id, so a void call like setItem threw and was swallowed by
    /// the caller's best-effort catch — which would have let a test claim the selection persisted
    /// when nothing had been written at all.
    /// </summary>
    private sealed class FakeJsRuntime : Microsoft.JSInterop.IJSRuntime
    {
        public string? StoredWorldId { get; set; }

        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public string? LastWriteTo(string key) => Calls
            .Where(c => c.Identifier == "localStorage.setItem"
                && c.Args.Length > 0 && (c.Args[0] as string) == key)
            .Select(c => c.Args.ElementAtOrDefault(1) as string)
            .LastOrDefault();

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return identifier switch
            {
                "localStorage.getItem" => ValueTask.FromResult((TValue)(object?)StoredWorldId!),
                _ => ValueTask.FromResult(default(TValue)!),
            };
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
