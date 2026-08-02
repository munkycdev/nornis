using System.Net;
using System.Text;
using Nornis.Web.ApiClient;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The client is the one place that sees every API answer, so it is where "this session no longer
/// works" is learned. The rules here are what the pollers and the nav banner build on: a 401 marks
/// the session expired, a success clears it, and nothing else moves it — a 403 or a 500 says
/// something about the request, not about whether the caller is authenticated.
/// </summary>
[TestFixture]
public class AuthSessionStateTests
{
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        public HttpStatusCode Respond { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(Respond)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
    }

    private ScriptedHandler _handler = null!;
    private AuthSessionState _session = null!;
    private NornisApiClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new ScriptedHandler();
        _session = new AuthSessionState();
        _client = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") },
            new ViewAsState(), new ActivitySignal(), _session);
    }

    [Test]

    [Category("Authorization")]
    public async Task AnUnauthorizedResponse_MarksTheSessionExpired()
    {
        _handler.Respond = HttpStatusCode.Unauthorized;

        await _client.GetWorldsAsync();

        Assert.That(_session.Expired, Is.True);
    }

    [Test]

    [Category("Authorization")]
    public async Task ASuccessfulResponse_ClearsTheExpiredState()
    {
        // The self-healing path: a failed token refresh can be transient (an Auth0 outage), and
        // the next call goes back through the refresher. One working answer ends the alarm.
        _handler.Respond = HttpStatusCode.Unauthorized;
        await _client.GetWorldsAsync();

        _handler.Respond = HttpStatusCode.OK;
        await _client.GetWorldsAsync();

        Assert.That(_session.Expired, Is.False);
    }

    [TestCase(HttpStatusCode.Forbidden)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.NotFound)]

    [Category("Authorization")]
    public async Task OtherFailures_DoNotTouchTheState(HttpStatusCode status)
    {
        // In both directions: they must not raise the alarm, and they must not clear a real one —
        // a 403 during view-as-player is routine, and treating it as "authenticated again" would
        // flap the banner.
        _handler.Respond = status;
        await _client.GetWorldsAsync();
        Assert.That(_session.Expired, Is.False, "a non-401 failure is not an expired session");

        _handler.Respond = HttpStatusCode.Unauthorized;
        await _client.GetWorldsAsync();
        _handler.Respond = status;
        await _client.GetWorldsAsync();
        Assert.That(_session.Expired, Is.True, "only a success proves the credentials work again");
    }

    [Test]
    public void TheStateOnlyAnnouncesRealTransitions()
    {
        var changes = 0;
        _session.Changed += () => changes++;

        _session.NotifyAuthorized();      // already fine — nothing to say
        _session.NotifyUnauthorized();    // working -> expired
        _session.NotifyUnauthorized();    // still expired — a storm must not be a render storm
        _session.NotifyAuthorized();      // expired -> working
        _session.NotifyAuthorized();

        Assert.That(changes, Is.EqualTo(2));
    }
}
