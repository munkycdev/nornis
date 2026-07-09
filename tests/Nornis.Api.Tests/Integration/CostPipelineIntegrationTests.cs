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
/// Integration tests for the full cost request pipeline: controller → service → repository → in-memory DB.
/// These tests exercise the HTTP pipeline with seeded AiUsageRecords, verifying correct aggregation,
/// grouping, date filtering, and response shapes.
///
/// Validates: Requirements 3.1, 3.6, 5.1, 6.1, 7.1, 8.2, 8.3, 9.2, 9.3, 10.2
/// </summary>
[TestFixture]
public class CostPipelineIntegrationTests
{
    private NornisWebApplicationFactory _factory = null!;
    private CostPipelineScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _ = _factory.CreateClient();
        _scenario = await SetupCostPipelineScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _factory.Dispose();
    }

    private string CostsUrl => $"/api/worlds/{_scenario.World.Id}/costs";

    #region GET summary — correct aggregation values

    [Test]
    public async Task GetSummary_WithSeededRecords_ReturnsCorrectAggregation()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync($"{CostsUrl}/summary");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        // AllTime should include all 6 seeded records (GM sees all users)
        Assert.That(result!.AllTime.OperationCount, Is.EqualTo(6));
        Assert.That(result.AllTime.TotalInputTokens, Is.EqualTo(6000));
        Assert.That(result.AllTime.TotalOutputTokens, Is.EqualTo(3000));
        Assert.That(result.AllTime.TotalTokens, Is.EqualTo(9000));
        Assert.That(result.AllTime.TotalEstimatedCostUsd, Is.EqualTo(0.60m));
    }

    [Test]
    public async Task GetSummary_TodayPeriod_IncludesOnlyTodaysRecords()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync($"{CostsUrl}/summary");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        // Today should include the 2 records created today
        Assert.That(result!.Today.OperationCount, Is.EqualTo(2));
        Assert.That(result.Today.TotalInputTokens, Is.EqualTo(2000));
        Assert.That(result.Today.TotalOutputTokens, Is.EqualTo(1000));
        Assert.That(result.Today.TotalTokens, Is.EqualTo(3000));
        Assert.That(result.Today.TotalEstimatedCostUsd, Is.EqualTo(0.20m));
    }

    #endregion

    #region GET by-user — correct per-user grouping

    [Test]
    public async Task GetByUser_AsGm_ReturnsAllUsersGrouped()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync($"{CostsUrl}/by-user");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<UserCostResponse>>();
        Assert.That(results, Is.Not.Null);
        Assert.That(results!, Has.Count.EqualTo(2)); // GM (Kelda) and Player (Tavrin)

        var keldaResult = results!.FirstOrDefault(r => r.UserId == _scenario.GmUserId);
        Assert.That(keldaResult, Is.Not.Null);
        Assert.That(keldaResult!.Username, Is.EqualTo("Kelda"));
        Assert.That(keldaResult.Summary.OperationCount, Is.EqualTo(4));
        Assert.That(keldaResult.Summary.TotalInputTokens, Is.EqualTo(4000));
        Assert.That(keldaResult.Summary.TotalOutputTokens, Is.EqualTo(2000));
        Assert.That(keldaResult.Summary.TotalEstimatedCostUsd, Is.EqualTo(0.40m));

        var tavrinResult = results!.FirstOrDefault(r => r.UserId == _scenario.PlayerUserId);
        Assert.That(tavrinResult, Is.Not.Null);
        Assert.That(tavrinResult!.Username, Is.EqualTo("Tavrin"));
        Assert.That(tavrinResult.Summary.OperationCount, Is.EqualTo(2));
        Assert.That(tavrinResult.Summary.TotalInputTokens, Is.EqualTo(2000));
        Assert.That(tavrinResult.Summary.TotalOutputTokens, Is.EqualTo(1000));
        Assert.That(tavrinResult.Summary.TotalEstimatedCostUsd, Is.EqualTo(0.20m));
    }

    #endregion

    #region GET by-operation — correct per-operation-type grouping

    [Test]
    public async Task GetByOperation_AsGm_ReturnsAllOperationTypesGrouped()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync($"{CostsUrl}/by-operation");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<OperationTypeCostResponse>>();
        Assert.That(results, Is.Not.Null);
        Assert.That(results!, Has.Count.EqualTo(3)); // SourceExtraction, AskLoremaster, ArtifactSummary

        var extraction = results!.FirstOrDefault(r => r.OperationType == "SourceExtraction");
        Assert.That(extraction, Is.Not.Null);
        Assert.That(extraction!.Summary.OperationCount, Is.EqualTo(3));
        Assert.That(extraction.Summary.TotalInputTokens, Is.EqualTo(3000));

        var ask = results!.FirstOrDefault(r => r.OperationType == "AskLoremaster");
        Assert.That(ask, Is.Not.Null);
        Assert.That(ask!.Summary.OperationCount, Is.EqualTo(2));
        Assert.That(ask.Summary.TotalInputTokens, Is.EqualTo(2000));

        var summary = results!.FirstOrDefault(r => r.OperationType == "ArtifactSummary");
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Summary.OperationCount, Is.EqualTo(1));
        Assert.That(summary.Summary.TotalInputTokens, Is.EqualTo(1000));
    }

    #endregion

    #region GET by-model — correct per-model grouping

    [Test]
    public async Task GetByModel_AsGm_ReturnsAllModelsGrouped()
    {
        // Act
        var response = await _scenario.GmClient.GetAsync($"{CostsUrl}/by-model");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<ModelCostResponse>>();
        Assert.That(results, Is.Not.Null);
        Assert.That(results!, Has.Count.EqualTo(2)); // gpt-4o, gpt-4o-mini

        var gpt4o = results!.FirstOrDefault(r => r.Model == "gpt-4o");
        Assert.That(gpt4o, Is.Not.Null);
        Assert.That(gpt4o!.Summary.OperationCount, Is.EqualTo(4));
        Assert.That(gpt4o.Summary.TotalInputTokens, Is.EqualTo(4000));
        Assert.That(gpt4o.Summary.TotalOutputTokens, Is.EqualTo(2000));
        Assert.That(gpt4o.Summary.TotalEstimatedCostUsd, Is.EqualTo(0.40m));

        var gpt4oMini = results!.FirstOrDefault(r => r.Model == "gpt-4o-mini");
        Assert.That(gpt4oMini, Is.Not.Null);
        Assert.That(gpt4oMini!.Summary.OperationCount, Is.EqualTo(2));
        Assert.That(gpt4oMini.Summary.TotalInputTokens, Is.EqualTo(2000));
        Assert.That(gpt4oMini.Summary.TotalOutputTokens, Is.EqualTo(1000));
        Assert.That(gpt4oMini.Summary.TotalEstimatedCostUsd, Is.EqualTo(0.20m));
    }

    #endregion

    #region Date range filtering produces correct subset

    [Test]
    public async Task GetByOperation_WithDateRange_ReturnsFilteredSubset()
    {
        // Arrange — filter to only records from the last 3 days (excludes the 30-day-old records)
        var startDate = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-3).ToString("O"));
        var endDate = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"));

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"{CostsUrl}/by-operation?startDate={startDate}&endDate={endDate}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<OperationTypeCostResponse>>();
        Assert.That(results, Is.Not.Null);

        // Only 4 records are within the last 3 days (2 today + 2 yesterday)
        var totalOps = results!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(4));
    }

    [Test]
    public async Task GetByModel_WithStartDateOnly_FiltersFromStartDate()
    {
        // Arrange — start from 2 days ago (excludes the 30-day-old records)
        var startDate = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-2).ToString("O"));

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"{CostsUrl}/by-model?startDate={startDate}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<ModelCostResponse>>();
        Assert.That(results, Is.Not.Null);

        var totalOps = results!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(4)); // 2 today + 2 yesterday
    }

    [Test]
    public async Task GetByUser_WithEndDateOnly_FiltersToEndDate()
    {
        // Arrange — end 2 days ago (only includes the 30-day-old records)
        var endDate = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-2).ToString("O"));

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"{CostsUrl}/by-user?endDate={endDate}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<UserCostResponse>>();
        Assert.That(results, Is.Not.Null);

        var totalOps = results!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2)); // 2 records are 30 days old
    }

    #endregion

    #region Empty world — all zeros (not error)

    [Test]
    public async Task GetSummary_EmptyWorld_ReturnsAllZeros()
    {
        // Arrange — create a new world with no usage records
        var emptyWorldId = await CreateEmptyWorldForGmAsync();

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{emptyWorldId}/costs/summary");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        Assert.That(result!.AllTime.OperationCount, Is.EqualTo(0));
        Assert.That(result.AllTime.TotalInputTokens, Is.EqualTo(0));
        Assert.That(result.AllTime.TotalOutputTokens, Is.EqualTo(0));
        Assert.That(result.AllTime.TotalTokens, Is.EqualTo(0));
        Assert.That(result.AllTime.TotalEstimatedCostUsd, Is.EqualTo(0m));

        Assert.That(result.Today.OperationCount, Is.EqualTo(0));
        Assert.That(result.ThisWeek.OperationCount, Is.EqualTo(0));
        Assert.That(result.ThisMonth.OperationCount, Is.EqualTo(0));
    }

    [Test]
    public async Task GetByOperation_EmptyWorld_ReturnsEmptyList()
    {
        // Arrange
        var emptyWorldId = await CreateEmptyWorldForGmAsync();

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{emptyWorldId}/costs/by-operation");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var results = await response.Content.ReadFromJsonAsync<List<OperationTypeCostResponse>>();
        Assert.That(results, Is.Not.Null);
        Assert.That(results!, Is.Empty);
    }

    #endregion

    #region SQL-side aggregation matches manual sum for representative data

    [Test]
    public async Task GetSummary_AggregationMatchesManualSum()
    {
        // This test verifies that the EF Core LINQ projection produces the same
        // results as manually summing the seeded records.
        // Seeded data: 6 records total (GM sees all)
        //   - 4 records by Kelda: InputTokens=1000 each, OutputTokens=500 each, Cost=0.10 each
        //   - 2 records by Tavrin: InputTokens=1000 each, OutputTokens=500 each, Cost=0.10 each

        // Expected manual sums:
        var expectedTotalInput = 6 * 1000L;    // 6000
        var expectedTotalOutput = 6 * 500L;    // 3000
        var expectedTotalTokens = 6 * 1500L;   // 9000
        var expectedCost = 6 * 0.10m;          // 0.60
        var expectedCount = 6;

        // Act
        var response = await _scenario.GmClient.GetAsync($"{CostsUrl}/summary");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();
        Assert.That(result, Is.Not.Null);

        // Assert — AllTime aggregation matches manual sum
        Assert.That(result!.AllTime.TotalInputTokens, Is.EqualTo(expectedTotalInput));
        Assert.That(result.AllTime.TotalOutputTokens, Is.EqualTo(expectedTotalOutput));
        Assert.That(result.AllTime.TotalTokens, Is.EqualTo(expectedTotalTokens));
        Assert.That(result.AllTime.TotalEstimatedCostUsd, Is.EqualTo(expectedCost));
        Assert.That(result.AllTime.OperationCount, Is.EqualTo(expectedCount));
    }

    [Test]
    public async Task GetByUser_SumOfGroupsMatchesTotal()
    {
        // Act — get by-user and summary
        var userResponse = await _scenario.GmClient.GetAsync($"{CostsUrl}/by-user");
        var summaryResponse = await _scenario.GmClient.GetAsync($"{CostsUrl}/summary");

        Assert.That(userResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(summaryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var users = await userResponse.Content.ReadFromJsonAsync<List<UserCostResponse>>();
        var summary = await summaryResponse.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();

        // Assert — sum of per-user groups equals all-time total
        var sumInput = users!.Sum(u => u.Summary.TotalInputTokens);
        var sumOutput = users!.Sum(u => u.Summary.TotalOutputTokens);
        var sumTokens = users!.Sum(u => u.Summary.TotalTokens);
        var sumCost = users!.Sum(u => u.Summary.TotalEstimatedCostUsd);
        var sumOps = users!.Sum(u => u.Summary.OperationCount);

        Assert.That(sumInput, Is.EqualTo(summary!.AllTime.TotalInputTokens));
        Assert.That(sumOutput, Is.EqualTo(summary.AllTime.TotalOutputTokens));
        Assert.That(sumTokens, Is.EqualTo(summary.AllTime.TotalTokens));
        Assert.That(sumCost, Is.EqualTo(summary.AllTime.TotalEstimatedCostUsd));
        Assert.That(sumOps, Is.EqualTo(summary.AllTime.OperationCount));
    }

    [Test]
    public async Task GetByOperation_SumOfGroupsMatchesTotal()
    {
        // Act
        var opResponse = await _scenario.GmClient.GetAsync($"{CostsUrl}/by-operation");
        var summaryResponse = await _scenario.GmClient.GetAsync($"{CostsUrl}/summary");

        Assert.That(opResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(summaryResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var ops = await opResponse.Content.ReadFromJsonAsync<List<OperationTypeCostResponse>>();
        var summary = await summaryResponse.Content.ReadFromJsonAsync<TimePeriodSummaryResponse>();

        // Assert — sum of per-operation groups equals all-time total
        var sumOps = ops!.Sum(o => o.Summary.OperationCount);
        var sumInput = ops!.Sum(o => o.Summary.TotalInputTokens);
        var sumCost = ops!.Sum(o => o.Summary.TotalEstimatedCostUsd);

        Assert.That(sumOps, Is.EqualTo(summary!.AllTime.OperationCount));
        Assert.That(sumInput, Is.EqualTo(summary.AllTime.TotalInputTokens));
        Assert.That(sumCost, Is.EqualTo(summary.AllTime.TotalEstimatedCostUsd));
    }

    #endregion

    #region Helpers

    private async Task<Guid> CreateEmptyWorldForGmAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Silver Key Mystery",
            Description = "An empty world for testing",
            GameSystem = "D&D 5e",
            CreatedByUserId = _scenario.GmUserId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.Worlds.Add(world);

        db.WorldMembers.Add(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = world.Id,
            UserId = _scenario.GmUserId,
            Role = WorldRole.GM,
            JoinedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
        return world.Id;
    }

    #endregion

    #region Scenario Setup

    /// <summary>
    /// Sets up the cost pipeline test scenario:
    /// - World "Black Harbor Investigation"
    /// - GM user (Kelda) with 4 AiUsageRecords
    /// - Player user (Tavrin) with 2 AiUsageRecords
    /// - Records distributed across timestamps, operation types, and models
    ///
    /// Record layout:
    /// 1. Kelda, SourceExtraction, gpt-4o, today
    /// 2. Kelda, AskLoremaster, gpt-4o, today
    /// 3. Kelda, SourceExtraction, gpt-4o-mini, yesterday
    /// 4. Kelda, ArtifactSummary, gpt-4o, 30 days ago
    /// 5. Tavrin, SourceExtraction, gpt-4o-mini, yesterday
    /// 6. Tavrin, AskLoremaster, gpt-4o, 30 days ago
    ///
    /// Each record: InputTokens=1000, OutputTokens=500, TotalTokens=1500, Cost=0.10
    /// </summary>
    private static async Task<CostPipelineScenario> SetupCostPipelineScenarioAsync(
        NornisWebApplicationFactory factory)
    {
        var gmUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|gm-kelda-cost", "kelda@blackharbor.com", "Kelda");

        var playerUserId = await SourceTestHelpers.ProvisionUserAndGetIdAsync(
            factory, "auth0|player-tavrin-cost", "tavrin@blackharbor.com", "Tavrin");

        var world = await SourceTestHelpers.CreateTestWorldAsync(
            factory, gmUserId, name: "Black Harbor Investigation");

        // Update GM member to have a DisplayName (needed for username resolution)
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var gmMember = db.WorldMembers.First(m => m.UserId == gmUserId && m.WorldId == world.Id);
            gmMember.DisplayName = "Kelda";
            await db.SaveChangesAsync();
        }

        await SourceTestHelpers.AddWorldMemberAsync(
            factory, world.Id, playerUserId, WorldRole.Player,
            displayName: "Tavrin");

        var now = DateTimeOffset.UtcNow;
        var yesterday = now.AddDays(-1);
        var thirtyDaysAgo = now.AddDays(-30);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

            var records = new List<AiUsageRecord>
            {
                CreateRecord(world.Id, gmUserId, AiOperationType.SourceExtraction, "gpt-4o", now),
                CreateRecord(world.Id, gmUserId, AiOperationType.AskLoremaster, "gpt-4o", now),
                CreateRecord(world.Id, gmUserId, AiOperationType.SourceExtraction, "gpt-4o-mini", yesterday),
                CreateRecord(world.Id, gmUserId, AiOperationType.ArtifactSummary, "gpt-4o", thirtyDaysAgo),
                CreateRecord(world.Id, playerUserId, AiOperationType.SourceExtraction, "gpt-4o-mini", yesterday),
                CreateRecord(world.Id, playerUserId, AiOperationType.AskLoremaster, "gpt-4o", thirtyDaysAgo),
            };

            db.AiUsageRecords.AddRange(records);
            await db.SaveChangesAsync();
        }

        return new CostPipelineScenario
        {
            World = world,
            GmUserId = gmUserId,
            PlayerUserId = playerUserId,
            GmClient = factory.CreateAuthenticatedClient(
                sub: "auth0|gm-kelda-cost", email: "kelda@blackharbor.com", nickname: "Kelda"),
        };
    }

    private static AiUsageRecord CreateRecord(
        Guid worldId,
        Guid userId,
        AiOperationType operationType,
        string model,
        DateTimeOffset createdAt)
    {
        return new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            UserId = userId,
            OperationType = operationType,
            Model = model,
            InputTokens = 1000,
            OutputTokens = 500,
            TotalTokens = 1500,
            EstimatedCostUsd = 0.10m,
            DurationMs = 250,
            Succeeded = true,
            CreatedAt = createdAt
        };
    }

    #endregion
}

/// <summary>
/// Contains entities and HTTP clients for the cost pipeline integration tests.
/// </summary>
public class CostPipelineScenario
{
    public required World World { get; init; }
    public required Guid GmUserId { get; init; }
    public required Guid PlayerUserId { get; init; }
    public required HttpClient GmClient { get; init; }
}
