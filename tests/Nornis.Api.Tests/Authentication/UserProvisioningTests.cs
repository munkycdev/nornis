using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authentication;

[TestFixture]
public class UserProvisioningTests
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
    public async Task NewUser_IsCreatedFromJwtClaims()
    {
        // Arrange
        var sub = "auth0|captain-voss-new";
        var email = "voss@blackharbor.com";
        var nickname = "Captain Voss";

        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);

        // Act - make any authenticated request to trigger user provisioning
        var response = await client.GetAsync("/api/worlds");

        // Assert - request should succeed (not 401/503)
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));

        // Verify user was created in the database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0SubjectId == sub);

        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Auth0SubjectId, Is.EqualTo(sub));
        Assert.That(user.Username, Is.EqualTo(nickname));
        Assert.That(user.Email, Is.EqualTo(email));
    }

    [Test]
    public async Task ExistingUser_IsResolvedWithoutModification()
    {
        // Arrange
        var sub = "auth0|tavrin-existing";
        var email = "tavrin@example.com";
        var nickname = "Tavrin";

        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);

        // First request - creates the user
        await client.GetAsync("/api/worlds");

        // Record the user state after first request
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0SubjectId == sub);
            Assert.That(user, Is.Not.Null, "User should exist after first request");
        }

        // Act - second request with same sub should resolve existing user
        var response = await client.GetAsync("/api/worlds");

        // Assert
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));

        // Verify no duplicate user was created
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<NornisDbContext>();
        var users = await db2.Users.Where(u => u.Auth0SubjectId == sub).ToListAsync();

        Assert.That(users, Has.Count.EqualTo(1), "Only one user record should exist for the same sub");
        Assert.That(users[0].Username, Is.EqualTo(nickname));
        Assert.That(users[0].Email, Is.EqualTo(email));
    }

    [Test]
    public async Task MissingEmailClaim_Returns401()
    {
        // Arrange - generate token without email claim
        var token = TestJwtIssuer.GenerateTokenWithoutEmail(
            sub: "auth0|no-email-user",
            nickname: "NoEmail");

        var client = _factory.CreateClient();
        client.WithAuthToken(token);

        // Act
        var response = await client.GetAsync("/api/worlds");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task NicknameFallsBackToSub_WhenNicknameAbsent()
    {
        // Arrange - create token without nickname claim
        var sub = "auth0|silver-key-finder";
        var email = "finder@example.com";

        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: null);

        // Act
        var response = await client.GetAsync("/api/worlds");

        // Assert - request should succeed
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(response.StatusCode, Is.Not.EqualTo(HttpStatusCode.ServiceUnavailable));

        // Verify user was created with Username equal to sub
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Auth0SubjectId == sub);

        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Username, Is.EqualTo(sub), "Username should default to sub when nickname is absent");
        Assert.That(user.Email, Is.EqualTo(email));
    }
}
