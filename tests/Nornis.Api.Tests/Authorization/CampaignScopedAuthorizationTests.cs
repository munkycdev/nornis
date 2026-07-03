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
public class CampaignScopedAuthorizationTests
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
    /// A non-member accessing a campaign-scoped endpoint should receive 403 Forbidden.
    /// Validates: Requirements 8.1, 8.2
    /// </summary>
    [Test]
    public async Task NonMember_AccessingCampaignEndpoint_Returns403()
    {
        // Arrange - User A creates a campaign (becomes GM)
        var clientA = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-blackharbor",
            email: "gm@blackharbor.com",
            nickname: "GM Voss");

        var createResponse = await clientA.PostAsJsonAsync("/api/campaigns",
            new CreateCampaignRequest("Black Harbor Investigation", "A dark mystery", "D&D 5e"));
        Assert.That(createResponse.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var campaign = await createResponse.Content.ReadFromJsonAsync<CampaignResponse>();
        Assert.That(campaign, Is.Not.Null);

        // User B is not a member of the campaign
        var clientB = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-tavrin",
            email: "tavrin@outsider.com",
            nickname: "Tavrin");

        // Act - User B tries to access the campaign detail endpoint
        var response = await clientB.GetAsync($"/api/campaigns/{campaign!.Id}");

        // Assert - should get 403 Forbidden
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// A non-member accessing a campaign-scoped endpoint for a non-existent campaign
    /// should receive 403 Forbidden, NOT 404 (to avoid leaking campaign existence).
    /// Validates: Requirements 8.2, 8.5
    /// </summary>
    [Test]
    public async Task NonMember_AccessingNonExistentCampaign_Returns403NotFound()
    {
        // Arrange - authenticate a user who has no memberships
        var client = _factory.CreateAuthenticatedClient(
            sub: "auth0|lonely-observer",
            email: "observer@example.com",
            nickname: "Lonely Observer");

        // Trigger user provisioning first
        await client.GetAsync("/api/campaigns");

        var nonExistentCampaignId = Guid.NewGuid();

        // Act - try to access a campaign that does not exist at all
        var response = await client.GetAsync($"/api/campaigns/{nonExistentCampaignId}");

        // Assert - should get 403 (not 404) to avoid revealing campaign existence
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    /// <summary>
    /// A member accessing a campaign-scoped endpoint for a campaign that doesn't exist
    /// (but the membership record does) should receive 404 Not Found.
    /// This tests Req 8.6: members get 404 for non-existent campaigns.
    /// We seed a membership record without a corresponding campaign in the campaigns table.
    /// Validates: Requirements 8.6
    /// </summary>
    [Test]
    public async Task Member_AccessingNonExistentCampaign_Returns404()
    {
        // Arrange - Create a user by making an authenticated request
        var sub = "auth0|phantom-member";
        var email = "phantom@blackharbor.com";
        var nickname = "Phantom Member";

        var client = _factory.CreateAuthenticatedClient(sub: sub, email: email, nickname: nickname);

        // Trigger user provisioning
        await client.GetAsync("/api/campaigns");

        // Seed a CampaignMember record that points to a non-existent campaign
        // Since in-memory DB doesn't enforce FK constraints, this is valid
        var phantomCampaignId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var user = db.Users.First(u => u.Auth0SubjectId == sub);

            db.CampaignMembers.Add(new CampaignMember
            {
                Id = Guid.NewGuid(),
                CampaignId = phantomCampaignId,
                UserId = user.Id,
                Role = CampaignRole.Player,
                JoinedAt = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync();
        }

        // Act - the user IS a member, but the campaign doesn't exist
        var response = await client.GetAsync($"/api/campaigns/{phantomCampaignId}");

        // Assert - should get 404 Not Found (member sees campaign doesn't exist)
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
