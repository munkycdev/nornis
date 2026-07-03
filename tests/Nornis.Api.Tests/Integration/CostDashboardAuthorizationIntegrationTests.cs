using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Integration;

/// <summary>
/// Integration tests for CostsController authorization, role-based visibility, and the
/// cross-campaign endpoint. These tests exercise the full HTTP pipeline: JWT validation,
/// UserProvisioningMiddleware, CampaignMemberActionFilter, CostService role filtering,
/// and response assembly.
///
/// Validates: Requirements 1.1, 1.2, 1.3, 2.1, 2.2, 2.3, 2.4, 4.2
/// </summary>
[TestFixture]
public class CostDashboardAuthorizationIntegrationTests
{
    private NornisWebApplicationFactory _factory = null!;
    private CostDashboardTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SetupCostDashboardScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _scenario.ObserverClient.Dispose();
        _scenario.NonMemberClient.Dispose();
        _factory.Dispose();
    }

    private string SummaryUrl => $"/api/campaigns/{_scenario.Campaign.Id}/costs/summary";
    private string ByUserUrl => $"/api/campaigns/{_scenario.Campaign.Id}/costs/by-user";
    private string ByOperationUrl => $"/api/campaigns/{_scenario.Campaign.Id}/costs/by-operation";
    private string ByModelUrl => $"/api/campaigns/{_scenario.Campaign.Id}/costs/by-model";
    private string ByCampaignUrl => "/api/costs/by-campaign";

    #region Missing JWT → 401

    [Test]
    public async Task GetSummary_WithoutJwt_Returns401()
    {
        // Arrange — unauthenticated client (no bearer token)
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync(SummaryUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetByUser_WithoutJwt_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(ByUserUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetByCampaign_WithoutJwt_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(ByCampaignUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    #endregion

    #region Non-member → 403 without revealing campaign existence

    [Test]
    public async Task GetSummary_NonMember_Returns403()
    {
        // Arrange — user who is not a member of the campaign
        // Act
        var response = await _scenario.NonMemberClient.GetAsync(SummaryUrl);

        // Assert — 403, does not reveal campaign existence
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetSummary_NonMemberOnNonExistentCampaign_Returns403()
    {
        // Verify same response for non-existent campaign (no information leakage)
        var nonExistentUrl = $"/api/campaigns/{Guid.NewGuid()}/costs/summary";
        var response = await _scenario.NonMemberClient.GetAsync(nonExistentUrl);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByUser_NonMember_Returns403()
    {
        var response = await _scenario.NonMemberClient.GetAsync(ByUserUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByOperation_NonMember_Returns403()
    {
        var response = await _scenario.NonMemberClient.GetAsync(ByOperationUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task GetByModel_NonMember_Returns403()
    {
        var response = await _scenario.NonMemberClient.GetAsync(ByModelUrl);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region GM sees aggregated data for all users in campaign

    [Test]
    public async Task GetSummary_GmRole_SeesAllUsersAggregatedData()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync(SummaryUrl);

        // Assert — should succeed
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        // AllTime should include records from all users (GM + Player + Observer)
        // Total operation count: 2 (GM) + 2 (Player) + 1 (Observer) = 5
        Assert.That(result!.AllTime.OperationCount, Is.EqualTo(5));
        // Total input tokens: 100+200 (GM) + 150+250 (Player) + 80 (Observer) = 780
        Assert.That(result.AllTime.TotalInputTokens, Is.EqualTo(780));
    }

    [Test]
    public async Task GetByUser_GmRole_ReturnsAllUsersBreakdown()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync(ByUserUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<UserCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // GM sees breakdown for all 3 users who have records
        Assert.That(results!.Count, Is.EqualTo(3));

        var gmEntry = results.First(r => r.UserId == _scenario.GmUserId);
        var playerEntry = results.First(r => r.UserId == _scenario.PlayerUserId);
        var observerEntry = results.First(r => r.UserId == _scenario.ObserverUserId);

        Assert.That(gmEntry.Summary.OperationCount, Is.EqualTo(2));
        Assert.That(playerEntry.Summary.OperationCount, Is.EqualTo(2));
        Assert.That(observerEntry.Summary.OperationCount, Is.EqualTo(1));
    }

    #endregion

    #region Player sees only their own usage data

    [Test]
    public async Task GetSummary_PlayerRole_SeesOnlyOwnData()
    {
        // Act
        var response = await _scenario.PlayerClient.GetAsync(SummaryUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        // Player should only see their 2 records: 150 + 250 = 400 input tokens
        Assert.That(result!.AllTime.OperationCount, Is.EqualTo(2));
        Assert.That(result.AllTime.TotalInputTokens, Is.EqualTo(400));
    }

    [Test]
    public async Task GetByUser_PlayerRole_ReturnsOnlyOwnSummary()
    {
        // Act
        var response = await _scenario.PlayerClient.GetAsync(ByUserUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<UserCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // Player sees only their own entry
        Assert.That(results!.Count, Is.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(_scenario.PlayerUserId));
        Assert.That(results[0].Summary.OperationCount, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByOperation_PlayerRole_SeesOnlyOwnUsageByOperationType()
    {
        // Act
        var response = await _scenario.PlayerClient.GetAsync(ByOperationUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<OperationTypeCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // Player has 2 records: 1 AskLoremaster + 1 SourceExtraction
        var totalOps = results!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByModel_PlayerRole_SeesOnlyOwnUsageByModel()
    {
        // Act
        var response = await _scenario.PlayerClient.GetAsync(ByModelUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<ModelCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // Player total operations across all models should be 2
        var totalOps = results!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    #endregion

    #region Observer sees only their own usage data

    [Test]
    public async Task GetSummary_ObserverRole_SeesOnlyOwnData()
    {
        // Act
        var response = await _scenario.ObserverClient.GetAsync(SummaryUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        // Observer should only see their 1 record: 80 input tokens
        Assert.That(result!.AllTime.OperationCount, Is.EqualTo(1));
        Assert.That(result.AllTime.TotalInputTokens, Is.EqualTo(80));
    }

    [Test]
    public async Task GetByUser_ObserverRole_ReturnsOnlyOwnSummary()
    {
        // Act
        var response = await _scenario.ObserverClient.GetAsync(ByUserUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<UserCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // Observer sees only their own entry
        Assert.That(results!.Count, Is.EqualTo(1));
        Assert.That(results[0].UserId, Is.EqualTo(_scenario.ObserverUserId));
        Assert.That(results[0].Summary.OperationCount, Is.EqualTo(1));
    }

    #endregion

    #region Cross-campaign endpoint returns only GM-role campaigns

    [Test]
    public async Task GetByCampaign_ReturnsOnlyGmRoleCampaigns()
    {
        // Act — GM user (Kelda) is GM on "Black Harbor Investigation"
        //        and Player on "Silver Key Mystery"
        var response = await _scenario.GmClient.GetAsync(ByCampaignUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<CampaignCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // Should only contain the campaign where user is GM (Black Harbor Investigation)
        // Should NOT contain the campaign where user is Player (Silver Key Mystery)
        Assert.That(results!.Count, Is.EqualTo(1));
        Assert.That(results[0].CampaignId, Is.EqualTo(_scenario.Campaign.Id));
        Assert.That(results[0].CampaignName, Is.EqualTo("Black Harbor Investigation"));
    }

    [Test]
    public async Task GetByCampaign_UserWithNoGmCampaigns_ReturnsEmptyList()
    {
        // Act — Player user (Tavrin) is only Player, never GM
        var response = await _scenario.PlayerClient.GetAsync(ByCampaignUrl);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<CampaignCostResponse>>();
        Assert.That(results, Is.Not.Null);
        Assert.That(results!.Count, Is.EqualTo(0));
    }

    #endregion

    #region Test Setup

    /// <summary>
    /// Sets up a complete Cost Dashboard test scenario with:
    /// - Campaign "Black Harbor Investigation" with Kelda (GM), Tavrin (Player), Jorin (Observer)
    /// - A second campaign "Silver Key Mystery" where Kelda is a Player (not GM)
    /// - AiUsageRecords distributed across users for the primary campaign
    /// - AiUsageRecords in the second campaign (should NOT appear in cross-campaign for Kelda as Player)
    /// </summary>
    private static async Task<CostDashboardTestScenario> SetupCostDashboardScenarioAsync(
        NornisWebApplicationFactory factory)
    {
        // Provision users
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-kelda-cost", "kelda@blackharbor.com", "Kelda");

        var playerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|player-tavrin-cost", "tavrin@blackharbor.com", "Tavrin");

        var observerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|observer-jorin-cost", "jorin@blackharbor.com", "Jorin");

        var nonMemberUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|nonmember-stranger-cost", "stranger@elsewhere.com", "Stranger");

        // Create primary campaign with Kelda as GM
        var campaign = await SourceTestHelpers.CreateTestCampaignAsync(factory, gmUserId);

        // Add player and observer members to the primary campaign
        await SourceTestHelpers.AddCampaignMemberAsync(
            factory, campaign.Id, playerUserId, CampaignRole.Player,
            displayName: "Tavrin", characterName: "Tavrin the Bold");

        await SourceTestHelpers.AddCampaignMemberAsync(
            factory, campaign.Id, observerUserId, CampaignRole.Observer,
            displayName: "Jorin");

        // Create a second campaign where Kelda is a Player (not GM)
        // This tests that cross-campaign only returns GM campaigns
        var secondCampaignCreatorId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|other-gm-cost", "othergm@silverkey.com", "OtherGM");

        var secondCampaign = await SourceTestHelpers.CreateTestCampaignAsync(
            factory, secondCampaignCreatorId,
            name: "Silver Key Mystery",
            description: "A mystery involving the Silver Key");

        // Add Kelda as a Player in the second campaign
        await SourceTestHelpers.AddCampaignMemberAsync(
            factory, secondCampaign.Id, gmUserId, CampaignRole.Player,
            displayName: "Kelda");

        // Seed AiUsageRecords in the primary campaign
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

            // GM (Kelda) records — 2 records
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                UserId = gmUserId,
                OperationType = AiOperationType.SourceExtraction,
                Model = "gpt-4o",
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                EstimatedCostUsd = 0.005m,
                DurationMs = 1200,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
            });

            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                UserId = gmUserId,
                OperationType = AiOperationType.ArtifactSummary,
                Model = "gpt-4o-mini",
                InputTokens = 200,
                OutputTokens = 100,
                TotalTokens = 300,
                EstimatedCostUsd = 0.002m,
                DurationMs = 800,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
            });

            // Player (Tavrin) records — 2 records
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                UserId = playerUserId,
                OperationType = AiOperationType.AskLoremaster,
                Model = "gpt-4o",
                InputTokens = 150,
                OutputTokens = 75,
                TotalTokens = 225,
                EstimatedCostUsd = 0.004m,
                DurationMs = 1500,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-2)
            });

            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                UserId = playerUserId,
                OperationType = AiOperationType.SourceExtraction,
                Model = "gpt-4o",
                InputTokens = 250,
                OutputTokens = 125,
                TotalTokens = 375,
                EstimatedCostUsd = 0.008m,
                DurationMs = 2000,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-45)
            });

            // Observer (Jorin) records — 1 record
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                UserId = observerUserId,
                OperationType = AiOperationType.AskLoremaster,
                Model = "gpt-4o-mini",
                InputTokens = 80,
                OutputTokens = 40,
                TotalTokens = 120,
                EstimatedCostUsd = 0.001m,
                DurationMs = 900,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-15)
            });

            // Second campaign records (Kelda is Player here, not GM)
            db.AiUsageRecords.Add(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                CampaignId = secondCampaign.Id,
                UserId = gmUserId,
                OperationType = AiOperationType.AskLoremaster,
                Model = "gpt-4o",
                InputTokens = 300,
                OutputTokens = 150,
                TotalTokens = 450,
                EstimatedCostUsd = 0.010m,
                DurationMs = 1800,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddHours(-3)
            });

            await db.SaveChangesAsync();
        }

        return new CostDashboardTestScenario
        {
            Campaign = campaign,
            SecondCampaign = secondCampaign,
            GmUserId = gmUserId,
            PlayerUserId = playerUserId,
            ObserverUserId = observerUserId,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-kelda-cost", email: "kelda@blackharbor.com", nickname: "Kelda"),
            PlayerClient = factory.CreateAuthenticatedClient(
                sub: "auth0|player-tavrin-cost", email: "tavrin@blackharbor.com", nickname: "Tavrin"),
            ObserverClient = factory.CreateAuthenticatedClient(
                sub: "auth0|observer-jorin-cost", email: "jorin@blackharbor.com", nickname: "Jorin"),
            NonMemberClient = factory.CreateAuthenticatedClient(
                sub: "auth0|nonmember-stranger-cost", email: "stranger@elsewhere.com", nickname: "Stranger")
        };
    }

    #endregion
}

/// <summary>
/// Contains all entities and pre-configured HTTP clients for cost dashboard integration tests.
/// </summary>
public class CostDashboardTestScenario
{
    public required Campaign Campaign { get; init; }
    public required Campaign SecondCampaign { get; init; }
    public required Guid GmUserId { get; init; }
    public required Guid PlayerUserId { get; init; }
    public required Guid ObserverUserId { get; init; }
    public required HttpClient GmClient { get; init; }
    public required HttpClient PlayerClient { get; init; }
    public required HttpClient ObserverClient { get; init; }
    public required HttpClient NonMemberClient { get; init; }
}
