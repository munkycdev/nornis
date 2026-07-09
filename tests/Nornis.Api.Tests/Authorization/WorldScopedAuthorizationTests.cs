using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Authorization;

[TestFixture]
public class WorldScopedAuthorizationTests
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

    /// <summary>
    /// A non-member accessing a world-scoped endpoint should receive 403 Forbidden.
    /// Validates: Requirements 8.1, 8.2
    /// </summary>
    [Test]
    public async Task NonMember_AccessingWorldEndpoint_Returns403()
    {
        // Arrange - User A creates a world (becomes GM)
        var clientA = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-blackharbor",
            email: "gm@blackharbor.com",
            nickname: "GM Voss");

        var createResponse = await clientA.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest("Black Harbor Investigation", "A dark mystery", "D&D 5e"));
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var world = await createResponse.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world, Is.Not.Null);

        // User B is not a member of the world
        var clientB = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-tavrin",
            email: "tavrin@outsider.com",
            nickname: "Tavrin");

        // Act - User B tries to access the world detail endpoint
        var response = await clientB.GetAsync($"/api/worlds/{world!.Id}");

        // Assert - should get 403 Forbidden
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// A non-member accessing a world-scoped endpoint for a non-existent world
    /// should receive 403 Forbidden, NOT 404 (to avoid leaking world existence).
    /// Validates: Requirements 8.2, 8.5
    /// </summary>
    [Test]
    public async Task NonMember_AccessingNonExistentWorld_Returns403NotFound()
    {
        // Arrange - authenticate a user who has no memberships
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|lonely-observer",
            email: "observer@example.com",
            nickname: "Lonely Observer");

        // Trigger user provisioning first
        await client.GetAsync("/api/worlds");

        var nonExistentWorldId = Guid.NewGuid();

        // Act - try to access a world that does not exist at all
        var response = await client.GetAsync($"/api/worlds/{nonExistentWorldId}");

        // Assert - should get 403 (not 404) to avoid revealing world existence
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// A member accessing a world-scoped endpoint for a world that doesn't exist
    /// (but the membership record does) should receive 404 Not Found.
    /// This tests Req 8.6: members get 404 for non-existent worlds.
    /// We seed a membership record without a corresponding world in the worlds table.
    /// Validates: Requirements 8.6
    /// </summary>
    [Test]
    public async Task Member_AccessingNonExistentWorld_Returns404()
    {
        // Arrange - Create a user by making an authenticated request
        var sub = "auth0|phantom-member";
        var email = "phantom@blackharbor.com";
        var nickname = "Phantom Member";

        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);

        // Trigger user provisioning
        await client.GetAsync("/api/worlds");

        // Seed a WorldMember record that points to a non-existent world
        // Since in-memory DB doesn't enforce FK constraints, this is valid
        var phantomWorldId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var user = db.Users.First(u => u.Auth0SubjectId == sub);

            db.WorldMembers.Add(new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = phantomWorldId,
                UserId = user.Id,
                Role = WorldRole.Player,
                JoinedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // Act - the user IS a member, but the world doesn't exist
        var response = await client.GetAsync($"/api/worlds/{phantomWorldId}");

        // Assert - should get 404 Not Found (member sees world doesn't exist)
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
