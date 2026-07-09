using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

[TestFixture]
public class WorldCrudTests
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

    #region World Creation

    [Test]
    public async Task CreateWorld_Returns201_WithCorrectFields()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-001",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var request = new CreateWorldRequest(
            Name: "Black Harbor Investigation",
            Description: "A dark mystery in the harbor district",
            GameSystem: "D&D 5e");

        // Act
        var response = await client.PostAsJsonAsync("/api/worlds", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world, Is.Not.Null);
        Assert.That(world!.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(world.Name, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(world.Description, Is.EqualTo("A dark mystery in the harbor district"));
        Assert.That(world.GameSystem, Is.EqualTo("D&D 5e"));
        Assert.That(world.CreatedByUserId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(world.CreatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(world.UpdatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(world.MyRole, Is.EqualTo("GM"));
    }

    [Test]
    public async Task CreateWorld_WithEmptyName_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-001",
            email: "tavrin@example.com");

        var request = new CreateWorldRequest(Name: "");

        // Act
        var response = await client.PostAsJsonAsync("/api/worlds", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateWorld_WithWhitespaceName_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-002",
            email: "tavrin@example.com");

        var request = new CreateWorldRequest(Name: "   ");

        // Act
        var response = await client.PostAsJsonAsync("/api/worlds", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateWorld_WithNameExceeding100Chars_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-003",
            email: "tavrin@example.com");

        var longName = new string('A', 101);
        var request = new CreateWorldRequest(Name: longName);

        // Act
        var response = await client.PostAsJsonAsync("/api/worlds", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion

    #region World Listing

    [Test]
    public async Task ListWorlds_ReturnsOnlyUserWorlds()
    {
        // Arrange - User A creates a world
        var clientA = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-list",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateWorldRequest(
            Name: "Black Harbor Investigation",
            Description: "Voss's world");

        await clientA.PostAsJsonAsync("/api/worlds", createRequest);

        // User B creates a different world
        var clientB = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-list",
            email: "tavrin@example.com",
            nickname: "Tavrin");

        var createRequestB = new CreateWorldRequest(
            Name: "Silver Key Mystery",
            Description: "Tavrin's world");

        await clientB.PostAsJsonAsync("/api/worlds", createRequestB);

        // Act - User A lists their worlds
        var response = await clientA.GetAsync("/api/worlds");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var worlds = await response.Content.ReadFromJsonAsync<List<WorldListItemResponse>>();
        Assert.That(worlds, Is.Not.Null);
        Assert.That(worlds!.Count, Is.EqualTo(1));
        Assert.That(worlds[0].Name, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(worlds[0].MyRole, Is.EqualTo("GM"));
    }

    [Test]
    public async Task ListWorlds_ReturnsEmptyList_WhenUserHasNoWorlds()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|lonely-observer",
            email: "observer@example.com");

        // Act
        var response = await client.GetAsync("/api/worlds");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var worlds = await response.Content.ReadFromJsonAsync<List<WorldListItemResponse>>();
        Assert.That(worlds, Is.Not.Null);
        Assert.That(worlds!, Is.Empty);
    }

    #endregion

    #region World Detail

    [Test]
    public async Task GetWorldDetail_ReturnsWorld_WithMemberRole()
    {
        // Arrange - Create a world (creator becomes GM)
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-detail",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateWorldRequest(
            Name: "Black Harbor Investigation",
            Description: "Dark secrets in the harbor",
            GameSystem: "D&D 5e");

        var createResponse = await client.PostAsJsonAsync("/api/worlds", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WorldResponse>();

        // Act
        var response = await client.GetAsync($"/api/worlds/{created!.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world, Is.Not.Null);
        Assert.That(world!.Id, Is.EqualTo(created.Id));
        Assert.That(world.Name, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(world.Description, Is.EqualTo("Dark secrets in the harbor"));
        Assert.That(world.GameSystem, Is.EqualTo("D&D 5e"));
        Assert.That(world.MyRole, Is.EqualTo("GM"));
    }

    #endregion

    #region World Update

    [Test]
    public async Task UpdateWorld_ByGm_Succeeds()
    {
        // Arrange - Create a world (creator is GM)
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-update",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateWorldRequest(
            Name: "Black Harbor Investigation",
            Description: "Original description",
            GameSystem: "D&D 5e");

        var createResponse = await client.PostAsJsonAsync("/api/worlds", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WorldResponse>();

        var updateRequest = new UpdateWorldRequest(
            Name: "Black Harbor Conspiracy",
            Description: "Updated description",
            GameSystem: "Pathfinder 2e");

        // Act
        var response = await client.PutAsJsonAsync($"/api/worlds/{created!.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Name, Is.EqualTo("Black Harbor Conspiracy"));
        Assert.That(updated.Description, Is.EqualTo("Updated description"));
        Assert.That(updated.GameSystem, Is.EqualTo("Pathfinder 2e"));
        Assert.That(updated.UpdatedAt, Is.GreaterThanOrEqualTo(created.UpdatedAt));
    }

    [Test]
    public async Task UpdateWorld_ByNonGm_Returns403()
    {
        // Arrange - GM creates world
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-auth",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateWorldRequest(Name: "Black Harbor Investigation");
        var createResponse = await gmClient.PostAsJsonAsync("/api/worlds", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WorldResponse>();

        // Add a player to the world
        var playerClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-tavrin-auth",
            email: "tavrin@example.com",
            nickname: "Tavrin");

        // First make a request so the player user is provisioned
        await playerClient.GetAsync("/api/worlds");

        // We need to get the player's UserId from the DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var playerUser = await db.Users.FirstAsync(u => u.Auth0SubjectId == "auth0|player-tavrin-auth");

            var addRequest = new AddWorldMemberRequest(
                UserId: playerUser.Id,
                Role: "Player");

            await gmClient.PostAsJsonAsync($"/api/worlds/{created!.Id}/members", addRequest);
        }

        // Act - Player tries to update world
        var updateRequest = new UpdateWorldRequest(Name: "Hijacked World");
        var response = await playerClient.PutAsJsonAsync($"/api/worlds/{created!.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdateWorld_ByNonMember_Returns403()
    {
        // Arrange - GM creates world
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-nonmember",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateWorldRequest(Name: "Black Harbor Investigation");
        var createResponse = await gmClient.PostAsJsonAsync("/api/worlds", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<WorldResponse>();

        // Non-member user
        var nonMemberClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-stranger",
            email: "stranger@example.com",
            nickname: "Stranger");

        // Act - Non-member tries to update world
        var updateRequest = new UpdateWorldRequest(Name: "Stolen World");
        var response = await nonMemberClient.PutAsJsonAsync($"/api/worlds/{created!.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdateWorld_NonExistentWorld_Returns403()
    {
        // Arrange - User is authenticated but the world doesn't exist
        // Per Req 8.5: non-member accessing non-existent world gets 403
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-404",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdateWorldRequest(Name: "Ghost World");

        // Act
        var response = await client.PutAsJsonAsync($"/api/worlds/{nonExistentId}", updateRequest);

        // Assert - Per Req 8.5, non-member gets 403 even if world doesn't exist
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion
}
