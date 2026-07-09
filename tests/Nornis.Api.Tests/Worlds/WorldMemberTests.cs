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
public class WorldMemberTests
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
    /// Creates a world as the GM user and returns the world ID.
    /// </summary>
    private async Task<Guid> CreateWorldAsGm(HttpClient gmClient)
    {
        var createRequest = new CreateWorldRequest(
            "Black Harbor Investigation",
            "A dark mystery in the harbor district",
            "D&D 5e");

        var response = await gmClient.PostAsJsonAsync("/api/worlds", createRequest);
        response.EnsureSuccessStatusCode();

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        return world!.Id;
    }

    /// <summary>
    /// Provisions a user by making an authenticated request, then returns their Nornis User ID from the DB.
    /// </summary>
    private async Task<Guid> ProvisionUserAndGetId(string sub, string email, string? nickname = null)
    {
        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);
        // Trigger user provisioning
        await client.GetAsync("/api/worlds");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.FirstAsync(u => u.Auth0SubjectId == sub);
        return user.Id;
    }

    [Test]
    public async Task AddMember_Returns201_WithCorrectDetails()
    {
        // Arrange - create a world as GM
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-001",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        // Provision a target user
        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-001",
            "tavrin@example.com",
            "Tavrin");

        // Act - GM adds the player
        var addRequest = new AddWorldMemberRequest(targetUserId, "Player");
        var response = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var member = await response.Content.ReadFromJsonAsync<WorldMemberResponse>();
        Assert.That(member, Is.Not.Null);
        Assert.That(member!.WorldId, Is.EqualTo(worldId));
        Assert.That(member.UserId, Is.EqualTo(targetUserId));
        Assert.That(member.Role, Is.EqualTo("Player"));
        Assert.That(member.JoinedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public async Task AddMember_ByNonGm_Returns403()
    {
        // Arrange - create world as GM, then add a Player
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-002",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        // Provision and add a Player to the world
        var playerUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-002",
            "tavrin@example.com",
            "Tavrin");

        var addPlayerRequest = new AddWorldMemberRequest(playerUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addPlayerRequest);

        // Provision another user to be added
        var anotherUserId = await ProvisionUserAndGetId(
            "auth0|observer-finn-002",
            "finn@example.com",
            "Finn");

        // Act - Player tries to add a member
        var playerClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-tavrin-002",
            email: "tavrin@example.com",
            nickname: "Tavrin");

        var addRequest = new AddWorldMemberRequest(anotherUserId, "Observer");
        var response = await playerClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task AddMember_Duplicate_Returns409()
    {
        // Arrange
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-003",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-003",
            "tavrin@example.com",
            "Tavrin");

        // Add the user as a Player
        var addRequest = new AddWorldMemberRequest(targetUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Act - try to add the same user again
        var response = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task AddMember_NonExistentUser_Returns404()
    {
        // Arrange
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-004",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        // Act - try to add a user that was never provisioned
        var nonExistentUserId = Guid.NewGuid();
        var addRequest = new AddWorldMemberRequest(nonExistentUserId, "Player");
        var response = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task RemoveMember_Returns204()
    {
        // Arrange
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-005",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-005",
            "tavrin@example.com",
            "Tavrin");

        var addRequest = new AddWorldMemberRequest(targetUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Act - GM removes the player
        var response = await gmClient.DeleteAsync($"/api/worlds/{worldId}/members/{targetUserId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task RemoveLastGm_Returns409()
    {
        // Arrange - world with only one GM (the creator)
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-006",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        // Get the GM user's ID
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var gmUser = await db.Users.FirstAsync(u => u.Auth0SubjectId == "auth0|gm-voss-006");

        // Act - GM tries to remove themselves (last GM)
        var response = await gmClient.DeleteAsync($"/api/worlds/{worldId}/members/{gmUser.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task UpdateMemberRole_ReturnsUpdatedDetails()
    {
        // Arrange
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-007",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-007",
            "tavrin@example.com",
            "Tavrin");

        var addRequest = new AddWorldMemberRequest(targetUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members", addRequest);

        // Act - GM updates role from Player to Observer
        var updateRequest = new UpdateWorldMemberRoleRequest("Observer");
        var response = await gmClient.PutAsJsonAsync($"/api/worlds/{worldId}/members/{targetUserId}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updatedMember = await response.Content.ReadFromJsonAsync<WorldMemberResponse>();
        Assert.That(updatedMember, Is.Not.Null);
        Assert.That(updatedMember!.UserId, Is.EqualTo(targetUserId));
        Assert.That(updatedMember.Role, Is.EqualTo("Observer"));
        Assert.That(updatedMember.WorldId, Is.EqualTo(worldId));
    }

    [Test]
    public async Task ListMembers_ReturnsOrderedByJoinedAtAscending()
    {
        // Arrange
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-008",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        // Add multiple members with slight delays to ensure ordering
        var player1Id = await ProvisionUserAndGetId(
            "auth0|player-tavrin-008",
            "tavrin@example.com",
            "Tavrin");
        await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members",
            new AddWorldMemberRequest(player1Id, "Player"));

        var player2Id = await ProvisionUserAndGetId(
            "auth0|player-finn-008",
            "finn@example.com",
            "Finn");
        await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members",
            new AddWorldMemberRequest(player2Id, "Observer"));

        // Act - list members
        var response = await gmClient.GetAsync($"/api/worlds/{worldId}/members");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var members = await response.Content.ReadFromJsonAsync<List<WorldMemberResponse>>();
        Assert.That(members, Is.Not.Null);
        Assert.That(members!, Has.Count.EqualTo(3)); // GM + 2 added members

        // Verify ordering by JoinedAt ascending
        for (var i = 0; i < members!.Count - 1; i++)
        {
            Assert.That(members[i].JoinedAt, Is.LessThanOrEqualTo(members[i + 1].JoinedAt),
                $"Member at index {i} should have JoinedAt <= member at index {i + 1}");
        }
    }

    [Test]
    public async Task ListMembers_ByNonMember_Returns403()
    {
        // Arrange - create world with one user
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-009",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var worldId = await CreateWorldAsGm(gmClient);

        // Create a user who is NOT a member of this world
        var outsiderClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rogue-009",
            email: "rogue@example.com",
            nickname: "Rogue");

        // Trigger provisioning for the outsider
        await outsiderClient.GetAsync("/api/worlds");

        // Act - outsider tries to list members
        var response = await outsiderClient.GetAsync($"/api/worlds/{worldId}/members");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
