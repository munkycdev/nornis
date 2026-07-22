using System.Net;
using System.Text;
using NUnit.Framework;
using Nornis.Web.ApiClient;
using Nornis.Web.State;

namespace Nornis.Web.Tests;

[TestFixture]
public class WorldStateTests
{
    private static WorldState CreateState() =>
        new(new NornisApiClient(new HttpClient { BaseAddress = new Uri("http://localhost") }));

    private static WorldState CreateState(StubApiHandler handler) =>
        new(new NornisApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") }));

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
        await state.EnsureLoadedAsync();
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

        await state.EnsureLoadedAsync();
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
        await state.EnsureLoadedAsync();

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
        await state.EnsureLoadedAsync();

        await state.LoadContinuityAsync();

        Assert.That(handler.AssessmentRequests, Is.EqualTo(2));
    }

    [Test]
    public async Task EnsureContinuityLoadedAsync_ExposesErrorOnFailure()
    {
        var handler = new StubApiHandler { AssessmentStatus = HttpStatusCode.InternalServerError };
        var state = CreateState(handler);

        await state.EnsureLoadedAsync();

        Assert.That(state.Continuity, Is.Null);
        Assert.That(state.ContinuityError, Is.Not.Null);
    }

    /// <summary>Serves a one-world list and a canned assessment, counting requests per endpoint.</summary>
    private sealed class StubApiHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public string MyRole { get; init; } = "GM";
        public HttpStatusCode AssessmentStatus { get; init; } = HttpStatusCode.OK;
        public int AssessmentRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == "/api/worlds")
            {
                return Task.FromResult(Json(
                    $$"""[{"id":"{{WorldId}}","name":"W","description":null,"gameSystem":null,"myRole":"{{MyRole}}"}]"""));
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
}
