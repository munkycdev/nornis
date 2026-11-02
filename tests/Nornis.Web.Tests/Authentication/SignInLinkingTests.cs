using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Web.ApiClient;
using Nornis.Web.Authentication;
using NUnit.Framework;

namespace Nornis.Web.Tests.Authentication;

/// <summary>
/// The Web's half of linking a sign-in (feature 33): what the challenge carries, how a claim's
/// answer becomes Profile's <c>?signin=</c> word, and that a claim's own bearer is never
/// swapped for the session's.
/// </summary>
[TestFixture]
public class SignInLinkingTests
{
    [Test]
    public void The_challenge_forces_the_login_screen_and_carries_the_code_and_the_session()
    {
        var properties = SignInLinking.Challenge("code-123", "oauth2|discord|dave");

        Assert.Multiple(() =>
        {
            // Without prompt=login Auth0 hands back the sign-in already in use, and the link
            // would claim the subject the account already has.
            Assert.That(properties.Prompt, Is.EqualTo("login"));
            Assert.That(properties.RedirectUri, Is.EqualTo("/profile"));
            Assert.That(properties.Items[SignInLinking.CodeItem], Is.EqualTo("code-123"));
            Assert.That(properties.Items[SignInLinking.FromItem], Is.EqualTo("oauth2|discord|dave"));
            Assert.That(SignInLinking.IsLink(properties), Is.True);
        });
    }

    [Test]
    public void An_ordinary_sign_in_is_not_a_link()
    {
        Assert.Multiple(() =>
        {
            Assert.That(SignInLinking.IsLink(null), Is.False);
            Assert.That(SignInLinking.IsLink(new AuthenticationProperties { RedirectUri = "/" }), Is.False);
        });
    }

    [TestCase("linked")]
    [TestCase("already-linked")]
    public void A_successful_claim_reports_the_apis_outcome(string outcome)
    {
        var result = ApiResult<SignInClaimDto>.Ok(new SignInClaimDto(outcome));

        Assert.That(SignInLinking.Outcome(result), Is.EqualTo(outcome));
    }

    [TestCase("sign_in_taken", "taken")]
    [TestCase("link_expired", "expired")]
    [TestCase("network_error", "failed")]
    [TestCase("anything_else", "failed")]
    public void A_refused_claim_maps_to_a_word_profile_knows(string code, string expected)
    {
        var result = ApiResult<SignInClaimDto>.Fail(new ApiError(code, "nope"));

        Assert.That(SignInLinking.Outcome(result), Is.EqualTo(expected));
    }

    [Test]
    public void The_outcome_lands_on_profile()
    {
        Assert.That(SignInLinking.OutcomeUrl("taken"), Is.EqualTo("/profile?signin=taken"));
    }

    [Test]
    public void The_subject_is_read_from_either_claim_name()
    {
        var mapped = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "a")], "test"));
        var raw = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "b")], "test"));

        Assert.Multiple(() =>
        {
            Assert.That(SignInLinking.Subject(mapped), Is.EqualTo("a"));
            Assert.That(SignInLinking.Subject(raw), Is.EqualTo("b"));
            Assert.That(SignInLinking.Subject(null), Is.Null);
        });
    }

    [Test]
    public async Task A_request_that_names_its_own_bearer_keeps_it()
    {
        // The claim is sent from a request that still carries the old session. If the handler
        // replaced its header, the API would see the session's subject and answer "already linked".
        var seen = await SendThroughHandler(new AuthenticationHeaderValue("Bearer", "new-sign-in-token"));

        Assert.That(seen?.ToString(), Is.EqualTo("Bearer new-sign-in-token"));
    }

    private static async Task<AuthenticationHeaderValue?> SendThroughHandler(AuthenticationHeaderValue? authorization)
    {
        // The context has no authentication services, so consulting the session's token at all
        // would throw: passing proves the handler never looked.
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var refresher = new Auth0TokenRefresher(
            new NoClients(),
            new ConfigurationBuilder().Build(),
            NullLogger<Auth0TokenRefresher>.Instance);
        var recorder = new Recorder();
        var handler = new BearerTokenHandler(accessor, refresher) { InnerHandler = recorder };
        using var invoker = new HttpMessageInvoker(handler);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.test/api/me/sign-ins/claim");
        request.Headers.Authorization = authorization;
        await invoker.SendAsync(request, CancellationToken.None);

        return recorder.Authorization;
    }

    private sealed class Recorder : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class NoClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("No refresh expected.");
    }
}
