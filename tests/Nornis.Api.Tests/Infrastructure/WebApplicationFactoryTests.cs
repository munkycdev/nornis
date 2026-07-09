using System.Net;
using NUnit.Framework;

namespace Nornis.Api.Tests.Infrastructure;

[TestFixture]
public class WebApplicationFactoryTests
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
    public async Task Factory_CreatesClient_Successfully()
    {
        // Arrange & Act
        var client = _factory.CreateClient();

        // Assert - just verify we can create a client without exceptions
        Assert.That(client, Is.Not.Null);
    }

    [Test]
    public async Task HealthEndpoint_AnonymousAccess_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task AuthenticatedClient_CanBeCreated_WithValidToken()
    {
        // Arrange & Act
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|captain-voss",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        // Assert
        Assert.That(client, Is.Not.Null);
        Assert.That(client.DefaultRequestHeaders.Authorization, Is.Not.Null);
        Assert.That(client.DefaultRequestHeaders.Authorization!.Scheme, Is.EqualTo("Bearer"));
    }

    [Test]
    public async Task UnauthenticatedRequest_ToProtectedEndpoint_Returns401()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act - any endpoint other than /health requires auth
        var response = await client.GetAsync("/api/worlds");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AuthenticatedRequest_ToProtectedEndpoint_DoesNotReturn401()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|tavrin-001",
            email: "tavrin@example.com",
            nickname: "Tavrin");

        // Act
        var response = await client.GetAsync("/api/worlds");

        // Assert - should NOT be 401 (might be 200 empty list or another valid status)
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
    }
}
