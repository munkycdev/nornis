using System.Net;
using System.Text;
using Nornis.Web.ApiClient;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// <c>GET /api/onboarding</c> was fetched independently by TutorialChecklist (in MainLayout, so
/// on every authenticated page) and OnboardingPrompt (on the dashboard), which meant the app's
/// landing route issued the same request two or three times per load. This holder collapses
/// them to one per circuit.
/// </summary>
[TestFixture]
public class OnboardingStateTests
{
    private static OnboardingState CreateState(StubOnboardingHandler handler) =>
        new(new NornisApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new ViewAsState(),
            new ActivitySignal(),
            new AuthSessionState()));

    [Test]
    public async Task ConcurrentCallers_ShareOneRequest()
    {
        // The real shape: two components initialising in the same render pass.
        var handler = new StubOnboardingHandler();
        var state = CreateState(handler);

        var results = await Task.WhenAll(state.GetAsync(), state.GetAsync(), state.GetAsync());

        Assert.That(handler.Requests, Is.EqualTo(1));
        Assert.That(results.Select(r => r.Value!.TutorialDismissed), Is.All.False);
    }

    [Test]
    public async Task SecondCallAfterCompletion_IsServedFromCache()
    {
        var handler = new StubOnboardingHandler();
        var state = CreateState(handler);

        await state.GetAsync();
        await state.GetAsync();

        Assert.That(handler.Requests, Is.EqualTo(1));
    }

    [Test]
    public async Task Invalidate_ForcesARefetch()
    {
        // Dismissal has to invalidate, or a sibling reading the cache later resurrects the
        // checklist for the rest of the circuit.
        var handler = new StubOnboardingHandler();
        var state = CreateState(handler);

        await state.GetAsync();
        state.Invalidate();
        await state.GetAsync();

        Assert.That(handler.Requests, Is.EqualTo(2));
    }

    [Test]
    public async Task FailedResponse_IsNotCached()
    {
        // A transient failure must not stick for the whole circuit — that would hide the
        // tutorial until the user reloaded the page.
        var handler = new StubOnboardingHandler { Status = HttpStatusCode.ServiceUnavailable };
        var state = CreateState(handler);

        var first = await state.GetAsync();
        handler.Status = HttpStatusCode.OK;
        var second = await state.GetAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.IsSuccess, Is.False);
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(handler.Requests, Is.EqualTo(2));
        });
    }

    private sealed class StubOnboardingHandler : HttpMessageHandler
    {
        public int Requests { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;

            if (Status != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(Status));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"promptSeen":false,"tutorialDismissed":false}""",
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
