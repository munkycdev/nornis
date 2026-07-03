using System.Net;
using System.Net.Http.Headers;
using NUnit.Framework;
using Nornis.Api.Tests.Infrastructure;

namespace Nornis.Api.Tests.Authentication;

[TestFixture]
public class JwtAuthenticationTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task ValidToken_GrantsAccess_ToProtectedEndpoint()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|captain-voss-001",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        // Act
        var response = await client.GetAsync("/api/campaigns");

        // Assert - valid token should not yield 401
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task MissingToken_Returns401_ForProtectedEndpoint()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/campaigns");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ExpiredToken_Returns401_ForProtectedEndpoint()
    {
        // Arrange
        var expiredToken = TestJwtIssuer.GenerateExpiredToken(
            sub: "auth0|expired-tavrin",
            email: "tavrin@example.com");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", expiredToken);

        // Act
        var response = await client.GetAsync("/api/campaigns");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task WrongIssuer_Returns401_ForProtectedEndpoint()
    {
        // Arrange
        var wrongIssuerToken = TestJwtIssuer.GenerateWrongIssuerToken(
            sub: "auth0|silver-key-thief",
            email: "thief@untrusted.com");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", wrongIssuerToken);

        // Act
        var response = await client.GetAsync("/api/campaigns");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AnonymousAccess_ToHealth_Succeeds()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }
}
