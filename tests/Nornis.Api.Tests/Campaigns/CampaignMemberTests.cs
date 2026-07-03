using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Campaigns;

[TestFixture]
public class CampaignMemberTests
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
    /// Creates a campaign as the GM user and returns the campaign ID.
    /// </summary>
    private async Task<Guid> CreateCampaignAsGm(HttpClient gmClient)
    {
        var createRequest = new CreateCampaignRequest(
            "Black Harbor Investigation",
            "A dark mystery in the harbor district",
            "D&D 5e");

        var response = await gmClient.PostAsJsonAsync("/api/campaigns", createRequest);
        response.EnsureSuccessStatusCode();

        var campaign = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        return campaign!.Id;
    }

    /// <summary>
    /// Provisions a user by making an authenticated request, then returns their Nornis User ID from the DB.
    /// </summary>
    private async Task<Guid> ProvisionUserAndGetId(string sub, string email, string? nickname = null)
    {
        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);
        // Trigger user provisioning
        await client.GetAsync("/api/campaigns");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var user = await db.Users.FirstAsync(u => u.Auth0SubjectId == sub);
        return user.Id;
    }

    [Test]
    public async Task AddMember_Returns201_WithCorrectDetails()
    {
        // Arrange - create a campaign as GM
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-001",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var campaignId = await CreateCampaignAsGm(gmClient);

        // Provision a target user
        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-001",
            "tavrin@example.com",
            "Tavrin");

        // Act - GM adds the player
        var addRequest = new AddCampaignMemberRequest(targetUserId, "Player");
        var response = await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var member = await response.Content.ReadFromJsonAsync<CampaignMemberResponse>();
        Assert.That(member, Is.Not.Null);
        Assert.That(member!.CampaignId, Is.EqualTo(campaignId));
        Assert.That(member.UserId, Is.EqualTo(targetUserId));
        Assert.That(member.Role, Is.EqualTo("Player"));
        Assert.That(member.JoinedAt, Is.Not.EqualTo(default(DateTimeOffset)));
    }

    [Test]
    public async Task AddMember_ByNonGm_Returns403()
    {
        // Arrange - create campaign as GM, then add a Player
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-002",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var campaignId = await CreateCampaignAsGm(gmClient);

        // Provision and add a Player to the campaign
        var playerUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-002",
            "tavrin@example.com",
            "Tavrin");

        var addPlayerRequest = new AddCampaignMemberRequest(playerUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addPlayerRequest);

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

        var addRequest = new AddCampaignMemberRequest(anotherUserId, "Observer");
        var response = await playerClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

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

        var campaignId = await CreateCampaignAsGm(gmClient);

        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-003",
            "tavrin@example.com",
            "Tavrin");

        // Add the user as a Player
        var addRequest = new AddCampaignMemberRequest(targetUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

        // Act - try to add the same user again
        var response = await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

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

        var campaignId = await CreateCampaignAsGm(gmClient);

        // Act - try to add a user that was never provisioned
        var nonExistentUserId = Guid.NewGuid();
        var addRequest = new AddCampaignMemberRequest(nonExistentUserId, "Player");
        var response = await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

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

        var campaignId = await CreateCampaignAsGm(gmClient);

        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-005",
            "tavrin@example.com",
            "Tavrin");

        var addRequest = new AddCampaignMemberRequest(targetUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

        // Act - GM removes the player
        var response = await gmClient.DeleteAsync($"/api/campaigns/{campaignId}/members/{targetUserId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    }

    [Test]
    public async Task RemoveLastGm_Returns409()
    {
        // Arrange - campaign with only one GM (the creator)
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-006",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var campaignId = await CreateCampaignAsGm(gmClient);

        // Get the GM user's ID
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var gmUser = await db.Users.FirstAsync(u => u.Auth0SubjectId == "auth0|gm-voss-006");

        // Act - GM tries to remove themselves (last GM)
        var response = await gmClient.DeleteAsync($"/api/campaigns/{campaignId}/members/{gmUser.Id}");

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

        var campaignId = await CreateCampaignAsGm(gmClient);

        var targetUserId = await ProvisionUserAndGetId(
            "auth0|player-tavrin-007",
            "tavrin@example.com",
            "Tavrin");

        var addRequest = new AddCampaignMemberRequest(targetUserId, "Player");
        await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members", addRequest);

        // Act - GM updates role from Player to Observer
        var updateRequest = new UpdateCampaignMemberRoleRequest("Observer");
        var response = await gmClient.PutAsJsonAsync($"/api/campaigns/{campaignId}/members/{targetUserId}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updatedMember = await response.Content.ReadFromJsonAsync<CampaignMemberResponse>();
        Assert.That(updatedMember, Is.Not.Null);
        Assert.That(updatedMember!.UserId, Is.EqualTo(targetUserId));
        Assert.That(updatedMember.Role, Is.EqualTo("Observer"));
        Assert.That(updatedMember.CampaignId, Is.EqualTo(campaignId));
    }

    [Test]
    public async Task ListMembers_ReturnsOrderedByJoinedAtAscending()
    {
        // Arrange
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-008",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var campaignId = await CreateCampaignAsGm(gmClient);

        // Add multiple members with slight delays to ensure ordering
        var player1Id = await ProvisionUserAndGetId(
            "auth0|player-tavrin-008",
            "tavrin@example.com",
            "Tavrin");
        await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members",
            new AddCampaignMemberRequest(player1Id, "Player"));

        var player2Id = await ProvisionUserAndGetId(
            "auth0|player-finn-008",
            "finn@example.com",
            "Finn");
        await gmClient.PostAsJsonAsync($"/api/campaigns/{campaignId}/members",
            new AddCampaignMemberRequest(player2Id, "Observer"));

        // Act - list members
        var response = await gmClient.GetAsync($"/api/campaigns/{campaignId}/members");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var members = await response.Content.ReadFromJsonAsync<List<CampaignMemberResponse>>();
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
        // Arrange - create campaign with one user
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-009",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var campaignId = await CreateCampaignAsGm(gmClient);

        // Create a user who is NOT a member of this campaign
        var outsiderClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-rogue-009",
            email: "rogue@example.com",
            nickname: "Rogue");

        // Trigger provisioning for the outsider
        await outsiderClient.GetAsync("/api/campaigns");

        // Act - outsider tries to list members
        var response = await outsiderClient.GetAsync($"/api/campaigns/{campaignId}/members");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
