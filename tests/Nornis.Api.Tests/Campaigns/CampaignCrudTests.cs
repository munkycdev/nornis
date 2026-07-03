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
public class CampaignCrudTests
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

    #region Campaign Creation

    [Test]
    public async Task CreateCampaign_Returns201_WithCorrectFields()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-001",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var request = new CreateCampaignRequest(
            Name: "Black Harbor Investigation",
            Description: "A dark mystery in the harbor district",
            GameSystem: "D&D 5e");

        // Act
        var response = await client.PostAsJsonAsync("/api/campaigns", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var campaign = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.That(campaign, Is.Not.Null);
        Assert.That(campaign!.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(campaign.Name, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(campaign.Description, Is.EqualTo("A dark mystery in the harbor district"));
        Assert.That(campaign.GameSystem, Is.EqualTo("D&D 5e"));
        Assert.That(campaign.CreatedByUserId, Is.Not.EqualTo(Guid.Empty));
        Assert.That(campaign.CreatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(campaign.UpdatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(campaign.MyRole, Is.EqualTo("GM"));
    }

    [Test]
    public async Task CreateCampaign_WithEmptyName_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-001",
            email: "tavrin@example.com");

        var request = new CreateCampaignRequest(Name: "");

        // Act
        var response = await client.PostAsJsonAsync("/api/campaigns", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateCampaign_WithWhitespaceName_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-002",
            email: "tavrin@example.com");

        var request = new CreateCampaignRequest(Name: "   ");

        // Act
        var response = await client.PostAsJsonAsync("/api/campaigns", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task CreateCampaign_WithNameExceeding100Chars_Returns400()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-003",
            email: "tavrin@example.com");

        var longName = new string('A', 101);
        var request = new CreateCampaignRequest(Name: longName);

        // Act
        var response = await client.PostAsJsonAsync("/api/campaigns", request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion

    #region Campaign Listing

    [Test]
    public async Task ListCampaigns_ReturnsOnlyUserCampaigns()
    {
        // Arrange - User A creates a campaign
        var clientA = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-list",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateCampaignRequest(
            Name: "Black Harbor Investigation",
            Description: "Voss's campaign");

        await clientA.PostAsJsonAsync("/api/campaigns", createRequest);

        // User B creates a different campaign
        var clientB = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-tavrin-list",
            email: "tavrin@example.com",
            nickname: "Tavrin");

        var createRequestB = new CreateCampaignRequest(
            Name: "Silver Key Mystery",
            Description: "Tavrin's campaign");

        await clientB.PostAsJsonAsync("/api/campaigns", createRequestB);

        // Act - User A lists their campaigns
        var response = await clientA.GetAsync("/api/campaigns");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var campaigns = await response.Content.ReadFromJsonAsync<List<CampaignListItemResponse>>();
        Assert.That(campaigns, Is.Not.Null);
        Assert.That(campaigns!.Count, Is.EqualTo(1));
        Assert.That(campaigns[0].Name, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(campaigns[0].MyRole, Is.EqualTo("GM"));
    }

    [Test]
    public async Task ListCampaigns_ReturnsEmptyList_WhenUserHasNoCampaigns()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|lonely-observer",
            email: "observer@example.com");

        // Act
        var response = await client.GetAsync("/api/campaigns");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var campaigns = await response.Content.ReadFromJsonAsync<List<CampaignListItemResponse>>();
        Assert.That(campaigns, Is.Not.Null);
        Assert.That(campaigns!, Is.Empty);
    }

    #endregion

    #region Campaign Detail

    [Test]
    public async Task GetCampaignDetail_ReturnsCampaign_WithMemberRole()
    {
        // Arrange - Create a campaign (creator becomes GM)
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-detail",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateCampaignRequest(
            Name: "Black Harbor Investigation",
            Description: "Dark secrets in the harbor",
            GameSystem: "D&D 5e");

        var createResponse = await client.PostAsJsonAsync("/api/campaigns", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CampaignResponse>();

        // Act
        var response = await client.GetAsync($"/api/campaigns/{created!.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var campaign = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.That(campaign, Is.Not.Null);
        Assert.That(campaign!.Id, Is.EqualTo(created.Id));
        Assert.That(campaign.Name, Is.EqualTo("Black Harbor Investigation"));
        Assert.That(campaign.Description, Is.EqualTo("Dark secrets in the harbor"));
        Assert.That(campaign.GameSystem, Is.EqualTo("D&D 5e"));
        Assert.That(campaign.MyRole, Is.EqualTo("GM"));
    }

    #endregion

    #region Campaign Update

    [Test]
    public async Task UpdateCampaign_ByGm_Succeeds()
    {
        // Arrange - Create a campaign (creator is GM)
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-update",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateCampaignRequest(
            Name: "Black Harbor Investigation",
            Description: "Original description",
            GameSystem: "D&D 5e");

        var createResponse = await client.PostAsJsonAsync("/api/campaigns", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CampaignResponse>();

        var updateRequest = new UpdateCampaignRequest(
            Name: "Black Harbor Conspiracy",
            Description: "Updated description",
            GameSystem: "Pathfinder 2e");

        // Act
        var response = await client.PutAsJsonAsync($"/api/campaigns/{created!.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await response.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Name, Is.EqualTo("Black Harbor Conspiracy"));
        Assert.That(updated.Description, Is.EqualTo("Updated description"));
        Assert.That(updated.GameSystem, Is.EqualTo("Pathfinder 2e"));
        Assert.That(updated.UpdatedAt, Is.GreaterThanOrEqualTo(created.UpdatedAt));
    }

    [Test]
    public async Task UpdateCampaign_ByNonGm_Returns403()
    {
        // Arrange - GM creates campaign
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-auth",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateCampaignRequest(Name: "Black Harbor Investigation");
        var createResponse = await gmClient.PostAsJsonAsync("/api/campaigns", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CampaignResponse>();

        // Add a player to the campaign
        var playerClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-tavrin-auth",
            email: "tavrin@example.com",
            nickname: "Tavrin");

        // First make a request so the player user is provisioned
        await playerClient.GetAsync("/api/campaigns");

        // We need to get the player's UserId from the DB
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var playerUser = await db.Users.FirstAsync(u => u.Auth0SubjectId == "auth0|player-tavrin-auth");

            var addRequest = new AddCampaignMemberRequest(
                UserId: playerUser.Id,
                Role: "Player");

            await gmClient.PostAsJsonAsync($"/api/campaigns/{created!.Id}/members", addRequest);
        }

        // Act - Player tries to update campaign
        var updateRequest = new UpdateCampaignRequest(Name: "Hijacked Campaign");
        var response = await playerClient.PutAsJsonAsync($"/api/campaigns/{created!.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdateCampaign_ByNonMember_Returns403()
    {
        // Arrange - GM creates campaign
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-nonmember",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var createRequest = new CreateCampaignRequest(Name: "Black Harbor Investigation");
        var createResponse = await gmClient.PostAsJsonAsync("/api/campaigns", createRequest);
        var created = await createResponse.Content.ReadFromJsonAsync<CampaignResponse>();

        // Non-member user
        var nonMemberClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-stranger",
            email: "stranger@example.com",
            nickname: "Stranger");

        // Act - Non-member tries to update campaign
        var updateRequest = new UpdateCampaignRequest(Name: "Stolen Campaign");
        var response = await nonMemberClient.PutAsJsonAsync($"/api/campaigns/{created!.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task UpdateCampaign_NonExistentCampaign_Returns403()
    {
        // Arrange - User is authenticated but the campaign doesn't exist
        // Per Req 8.5: non-member accessing non-existent campaign gets 403
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-404",
            email: "voss@blackharbor.com",
            nickname: "Captain Voss");

        var nonExistentId = Guid.NewGuid();
        var updateRequest = new UpdateCampaignRequest(Name: "Ghost Campaign");

        // Act
        var response = await client.PutAsJsonAsync($"/api/campaigns/{nonExistentId}", updateRequest);

        // Assert - Per Req 8.5, non-member gets 403 even if campaign doesn't exist
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion
}
