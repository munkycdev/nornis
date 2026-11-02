using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nornis.Api.Authentication;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Application.Configuration;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Bibliotaph;

/// <summary>
/// A token Bibliotaph holds for the Nornis API is valid for the same audience as the Web's, so
/// the default policy has to refuse it by its <c>azp</c>, and it must never provision a user
/// (feature 31, Requirement 6.2; security-and-permissions.md, 2026-11-02 amendment).
/// </summary>
[TestFixture]
[Category("Authorization")]
public class BibliotaphTokenGuardTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task BibliotaphToken_IsRefused_OnAnOrdinaryEndpoint()
    {
        const string sub = "auth0|bibliotaph-guard-gm";
        await _factory.CreateAuthenticatedClient(sub: sub).GetAsync("/api/worlds"); // provision the GM

        var response = await ClientWith(TestJwtIssuer.GenerateBibliotaphToken(sub)).GetAsync("/api/worlds");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task WebToken_WithItsOwnAzp_IsServed()
    {
        var token = TestJwtIssuer.GenerateToken("auth0|bibliotaph-guard-web", "web@example.com",
            [new Claim("azp", TestJwtIssuer.WebClientId)]);

        var response = await ClientWith(token).GetAsync("/api/worlds");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task BibliotaphToken_ForAnUnknownSubject_CreatesNoUser()
    {
        // Authorization refuses it before provisioning runs; on the provider routes, which the
        // policy lets through, provisioning answers 401 instead (Phase D's tests).
        const string sub = "auth0|bibliotaph-guard-stranger";

        var response = await ClientWith(TestJwtIssuer.GenerateBibliotaphToken(sub)).GetAsync("/api/worlds");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(await db.Users.AnyAsync(u => u.Auth0SubjectId == sub), Is.False);
    }

    [Test]
    public async Task WithNoProviderClientId_TheGuardIsOff()
    {
        // Sabotage check for the test above: the same token is served once nothing names
        // Bibliotaph's client, so the 403 really comes from the azp rule.
        using var unguarded = _factory.WithWebHostBuilder(b => b.UseSetting("Bibliotaph:ProviderClientId", ""));
        var token = TestJwtIssuer.GenerateToken("auth0|bibliotaph-guard-off", "off@example.com",
            [new Claim("azp", TestJwtIssuer.BibliotaphClientId)]);
        var client = unguarded.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/worlds");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [TestCase(TestJwtIssuer.BibliotaphClientId, "openid published:read", true)]
    [TestCase(TestJwtIssuer.BibliotaphClientId, "openid offline_access", false)]
    [TestCase(TestJwtIssuer.WebClientId, "openid published:read", false)]
    [TestCase(null, "published:read", false)]
    public async Task BibliotaphPolicy_NeedsBibliotaphsClient_AndTheScope(string? azp, string scope, bool allowed)
    {
        var claims = new List<Claim> { new("sub", "auth0|x"), new("scope", scope) };
        if (azp is not null)
        {
            claims.Add(new Claim("azp", azp));
        }

        var user = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var http = new DefaultHttpContext { RequestServices = _factory.Services };
        var authorization = _factory.Services.GetRequiredService<IAuthorizationService>();

        var result = await authorization.AuthorizeAsync(user, http, BibliotaphTokens.PolicyName);

        Assert.That(result.Succeeded, Is.EqualTo(allowed));
    }

    [Test]
    public void TheTestHost_NamesBibliotaphsClient()
    {
        var options = _factory.Services.GetRequiredService<IOptions<BibliotaphOptions>>().Value;
        Assert.That(options.ProviderClientId, Is.EqualTo(TestJwtIssuer.BibliotaphClientId));
    }

    private HttpClient ClientWith(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
