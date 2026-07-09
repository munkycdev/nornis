using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Property 1: Role-Based Record Filtering
///
/// For any world with AiUsageRecords from multiple users, when a non-GM user (Player or Observer)
/// requests cost data, the aggregated result SHALL include only records where UserId matches the
/// requesting user. When a GM requests cost data, the result SHALL include records from all users
/// in the world.
///
/// **Validates: Requirements 2.1, 2.2, 2.3, 2.4, 5.3, 6.5, 7.5**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 1: Role-based record filtering")]
public class CostServiceRoleFilteringPropertyTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 1: Role-based record filtering")]
    public void Gm_sees_all_users_records_in_summary(RoleFilteringScenario scenario)
    {
        // Arrange
        var (costService, _) = BuildCostService(scenario);

        // Act — GM requests summary
        var result = costService.GetSummaryAsync(
            scenario.WorldId,
            scenario.GmUserId,
            WorldRole.GM,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — GM sees all records (all-time has no date filter)
        Assert.That(result.IsSuccess, Is.True, "GM summary request should succeed");
        var allTime = result.Value!.AllTime;
        var expectedCount = scenario.Records.Count;
        Assert.That(allTime.OperationCount, Is.EqualTo(expectedCount),
            $"GM should see all {expectedCount} records, but saw {allTime.OperationCount}");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 1: Role-based record filtering")]
    public void Player_sees_only_own_records_in_summary(RoleFilteringScenario scenario)
    {
        // Arrange
        var (costService, _) = BuildCostService(scenario);

        // Act — Player requests summary
        var result = costService.GetSummaryAsync(
            scenario.WorldId,
            scenario.PlayerUserId,
            WorldRole.Player,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — Player sees only their own records
        Assert.That(result.IsSuccess, Is.True, "Player summary request should succeed");
        var allTime = result.Value!.AllTime;
        var expectedCount = scenario.Records.Count(r => r.UserId == scenario.PlayerUserId);
        Assert.That(allTime.OperationCount, Is.EqualTo(expectedCount),
            $"Player should see only {expectedCount} own records, but saw {allTime.OperationCount}");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 1: Role-based record filtering")]
    public void Observer_sees_only_own_records_in_summary(RoleFilteringScenario scenario)
    {
        // Arrange
        var (costService, _) = BuildCostService(scenario);

        // Act — Observer requests summary
        var result = costService.GetSummaryAsync(
            scenario.WorldId,
            scenario.ObserverUserId,
            WorldRole.Observer,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — Observer sees only their own records
        Assert.That(result.IsSuccess, Is.True, "Observer summary request should succeed");
        var allTime = result.Value!.AllTime;
        var expectedCount = scenario.Records.Count(r => r.UserId == scenario.ObserverUserId);
        Assert.That(allTime.OperationCount, Is.EqualTo(expectedCount),
            $"Observer should see only {expectedCount} own records, but saw {allTime.OperationCount}");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 1: Role-based record filtering")]
    public void Gm_sees_all_users_records_in_by_operation_type(RoleFilteringScenario scenario)
    {
        // Arrange
        var (costService, _) = BuildCostService(scenario);

        // Act — GM requests by operation type
        var result = costService.GetByOperationTypeAsync(
            scenario.WorldId,
            scenario.GmUserId,
            WorldRole.GM,
            null, null,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — sum of operation counts across groups equals total record count
        Assert.That(result.IsSuccess, Is.True, "GM by-operation request should succeed");
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(scenario.Records.Count),
            $"GM should see all {scenario.Records.Count} records across operation types, but saw {totalOps}");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 1: Role-based record filtering")]
    public void Player_sees_only_own_records_in_by_model(RoleFilteringScenario scenario)
    {
        // Arrange
        var (costService, _) = BuildCostService(scenario);

        // Act — Player requests by model
        var result = costService.GetByModelAsync(
            scenario.WorldId,
            scenario.PlayerUserId,
            WorldRole.Player,
            null, null,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — Player sees only their own records in by-model breakdown
        Assert.That(result.IsSuccess, Is.True, "Player by-model request should succeed");
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        var expectedCount = scenario.Records.Count(r => r.UserId == scenario.PlayerUserId);
        Assert.That(totalOps, Is.EqualTo(expectedCount),
            $"Player should see only {expectedCount} own records in by-model, but saw {totalOps}");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(RoleFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 1: Role-based record filtering")]
    public void Observer_sees_only_own_records_in_by_operation_type(RoleFilteringScenario scenario)
    {
        // Arrange
        var (costService, _) = BuildCostService(scenario);

        // Act — Observer requests by operation type
        var result = costService.GetByOperationTypeAsync(
            scenario.WorldId,
            scenario.ObserverUserId,
            WorldRole.Observer,
            null, null,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert — Observer sees only their own records
        Assert.That(result.IsSuccess, Is.True, "Observer by-operation request should succeed");
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        var expectedCount = scenario.Records.Count(r => r.UserId == scenario.ObserverUserId);
        Assert.That(totalOps, Is.EqualTo(expectedCount),
            $"Observer should see only {expectedCount} own records in by-operation, but saw {totalOps}");
    }

    private static (CostService, InMemoryAiUsageRecordRepository) BuildCostService(RoleFilteringScenario scenario)
    {
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository(memberRepo);

        // Seed world members: Kelda (GM), Tavrin (Player), Jorin (Observer)
        memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = scenario.WorldId,
            UserId = scenario.GmUserId,
            Role = WorldRole.GM,
            DisplayName = "Kelda",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-30)
        }, CancellationToken.None).GetAwaiter().GetResult();

        memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = scenario.WorldId,
            UserId = scenario.PlayerUserId,
            Role = WorldRole.Player,
            DisplayName = "Tavrin",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-14)
        }, CancellationToken.None).GetAwaiter().GetResult();

        memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = scenario.WorldId,
            UserId = scenario.ObserverUserId,
            Role = WorldRole.Observer,
            DisplayName = "Jorin",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
        }, CancellationToken.None).GetAwaiter().GetResult();

        // Seed AI usage records
        foreach (var record in scenario.Records)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        var logger = NullLogger<CostService>.Instance;

        var costService = new CostService(
            aiUsageRepo,
            memberRepo,
            worldRepo,
            logger);

        return (costService, aiUsageRepo);
    }
}

/// <summary>
/// Represents a role-based filtering test scenario with records distributed across multiple users.
/// </summary>
public record RoleFilteringScenario(
    Guid WorldId,
    Guid GmUserId,
    Guid PlayerUserId,
    Guid ObserverUserId,
    List<AiUsageRecord> Records);

/// <summary>
/// FsCheck Arbitrary for role-based filtering property tests.
/// Generates worlds with AiUsageRecords distributed across multiple users (GM, Player, Observer).
/// </summary>
public class RoleFilteringArbitraries
{
    private static readonly string[] Models = ["gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo"];

    public static Arbitrary<RoleFilteringScenario> RoleFilteringScenarios()
    {
        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from playerUserId in ArbMap.Default.GeneratorFor<Guid>()
            where playerUserId != gmUserId
            from observerUserId in ArbMap.Default.GeneratorFor<Guid>()
            where observerUserId != gmUserId && observerUserId != playerUserId
            from recordCount in Gen.Choose(3, 20)
            from records in GenRecords(worldId, gmUserId, playerUserId, observerUserId, recordCount)
            select new RoleFilteringScenario(worldId, gmUserId, playerUserId, observerUserId, records);

        return gen.ToArbitrary();
    }

    private static Gen<List<AiUsageRecord>> GenRecords(
        Guid worldId, Guid gmUserId, Guid playerUserId, Guid observerUserId, int count)
    {
        var userIds = new[] { gmUserId, playerUserId, observerUserId };

        var extraRecordGen =
            from userIdx in Gen.Choose(0, userIds.Length - 1)
            from record in GenSingleRecord(worldId, userIds[userIdx])
            select record;

        // Ensure at least one record per user, then fill remaining randomly
        var gen =
            from gmRecord in GenSingleRecord(worldId, gmUserId)
            from playerRecord in GenSingleRecord(worldId, playerUserId)
            from observerRecord in GenSingleRecord(worldId, observerUserId)
            from extraRecords in extraRecordGen.ListOf(Math.Max(0, count - 3))
            select new List<AiUsageRecord>(new[] { gmRecord, playerRecord, observerRecord }.Concat(extraRecords));

        return gen;
    }

    private static Gen<AiUsageRecord> GenSingleRecord(Guid worldId, Guid userId)
    {
        return
            from inputTokens in Gen.Choose(10, 5000)
            from outputTokens in Gen.Choose(10, 3000)
            from operationType in Gen.Elements(
                AiOperationType.SourceExtraction,
                AiOperationType.ArtifactSummary,
                AiOperationType.AskLoremaster,
                AiOperationType.SourceExtractionRepair)
            from modelIdx in Gen.Choose(0, Models.Length - 1)
            from daysAgo in Gen.Choose(0, 60)
            select new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = userId,
                OperationType = operationType,
                Model = Models[modelIdx],
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                EstimatedCostUsd = (inputTokens * 0.005m + outputTokens * 0.015m) / 1000m,
                DurationMs = 500,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo)
            };
    }
}
