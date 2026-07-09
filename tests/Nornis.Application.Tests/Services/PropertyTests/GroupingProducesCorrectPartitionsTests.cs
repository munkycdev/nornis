using FsCheck;
using FsCheck.Fluent;
using FsCheck.NUnit;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Property 4: Grouping Produces Correct Partitions
///
/// For any set of AiUsageRecords grouped by a dimension (operation type, model, or user),
/// the result SHALL contain exactly one entry per distinct key value that has at least one
/// matching record. The sum of OperationCount across all groups SHALL equal the total count
/// of matching records. No group SHALL have an OperationCount of zero.
///
/// **Validates: Requirements 4.1, 5.1, 6.1, 6.2, 7.1, 7.2**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 4: Grouping produces correct partitions")]
public class GroupingProducesCorrectPartitionsTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(GroupingScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 4: Grouping produces correct partitions")]
    public void GetByOperationType_ProducesOneGroupPerDistinctOperationType(GroupingScenario scenario)
    {
        // Arrange
        var (costService, _) = SetupServices(scenario);

        // Act
        var result = costService.GetByOperationTypeAsync(
            scenario.WorldId,
            scenario.GmUserId,
            WorldRole.GM,
            null, null,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByOperationType should succeed for GM.");

        var groups = result.Value!;
        var distinctOperationTypes = scenario.Records
            .Select(r => r.OperationType.ToString())
            .Distinct()
            .ToHashSet();

        // Exactly one entry per distinct key
        Assert.That(groups.Count, Is.EqualTo(distinctOperationTypes.Count),
            "Number of groups should equal number of distinct operation types.");

        // Sum of OperationCount across groups equals total record count
        var totalOperationCount = groups.Sum(g => g.Summary.OperationCount);
        Assert.That(totalOperationCount, Is.EqualTo(scenario.Records.Count),
            "Sum of OperationCount across groups should equal total record count.");

        // No group has zero OperationCount
        Assert.That(groups.All(g => g.Summary.OperationCount > 0), Is.True,
            "No group should have OperationCount of zero.");

        // Each group key is unique
        var groupKeys = groups.Select(g => g.OperationType).ToList();
        Assert.That(groupKeys.Distinct().Count(), Is.EqualTo(groupKeys.Count),
            "Each group key should be unique.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(GroupingScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 4: Grouping produces correct partitions")]
    public void GetByModel_ProducesOneGroupPerDistinctModel(GroupingScenario scenario)
    {
        // Arrange
        var (costService, _) = SetupServices(scenario);

        // Act
        var result = costService.GetByModelAsync(
            scenario.WorldId,
            scenario.GmUserId,
            WorldRole.GM,
            null, null,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByModel should succeed for GM.");

        var groups = result.Value!;
        var distinctModels = scenario.Records
            .Select(r => r.Model)
            .Distinct()
            .ToHashSet();

        // Exactly one entry per distinct key
        Assert.That(groups.Count, Is.EqualTo(distinctModels.Count),
            "Number of groups should equal number of distinct models.");

        // Sum of OperationCount across groups equals total record count
        var totalOperationCount = groups.Sum(g => g.Summary.OperationCount);
        Assert.That(totalOperationCount, Is.EqualTo(scenario.Records.Count),
            "Sum of OperationCount across groups should equal total record count.");

        // No group has zero OperationCount
        Assert.That(groups.All(g => g.Summary.OperationCount > 0), Is.True,
            "No group should have OperationCount of zero.");

        // Each group key is unique
        var groupKeys = groups.Select(g => g.Model).ToList();
        Assert.That(groupKeys.Distinct().Count(), Is.EqualTo(groupKeys.Count),
            "Each group key should be unique.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(GroupingScenarioArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 4: Grouping produces correct partitions")]
    public void GetByUser_ProducesOneGroupPerDistinctUser(GroupingScenario scenario)
    {
        // Arrange
        var (costService, _) = SetupServices(scenario);

        // Act
        var result = costService.GetByUserAsync(
            scenario.WorldId,
            scenario.GmUserId,
            WorldRole.GM,
            null, null,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByUser should succeed for GM.");

        var groups = result.Value!;
        var distinctUsers = scenario.Records
            .Where(r => r.UserId.HasValue)
            .Select(r => r.UserId!.Value)
            .Distinct()
            .ToHashSet();

        // Exactly one entry per distinct key
        Assert.That(groups.Count, Is.EqualTo(distinctUsers.Count),
            "Number of groups should equal number of distinct users.");

        // Sum of OperationCount across groups equals total record count
        var totalOperationCount = groups.Sum(g => g.Summary.OperationCount);
        Assert.That(totalOperationCount, Is.EqualTo(scenario.Records.Count),
            "Sum of OperationCount across groups should equal total record count.");

        // No group has zero OperationCount
        Assert.That(groups.All(g => g.Summary.OperationCount > 0), Is.True,
            "No group should have OperationCount of zero.");

        // Each group key is unique
        var groupKeys = groups.Select(g => g.UserId).ToList();
        Assert.That(groupKeys.Distinct().Count(), Is.EqualTo(groupKeys.Count),
            "Each group key should be unique.");
    }

    private static (CostService, InMemoryAiUsageRecordRepository) SetupServices(GroupingScenario scenario)
    {
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();

        // Seed usage records
        foreach (var record in scenario.Records)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Seed world members for username resolution
        foreach (var userId in scenario.UserIds)
        {
            memberRepo.CreateAsync(new WorldMember
            {
                Id = Guid.NewGuid(),
                WorldId = scenario.WorldId,
                UserId = userId,
                Role = userId == scenario.GmUserId ? WorldRole.GM : WorldRole.Player,
                DisplayName = $"User-{userId.ToString()[..8]}",
                JoinedAt = DateTimeOffset.UtcNow.AddDays(-7)
            }, CancellationToken.None).GetAwaiter().GetResult();
        }

        var costService = new CostService(
            aiUsageRepo,
            memberRepo,
            worldRepo,
            NullLogger<CostService>.Instance);

        return (costService, aiUsageRepo);
    }
}

/// <summary>
/// Input model for grouping property test scenarios.
/// </summary>
public record GroupingScenario(
    Guid WorldId,
    Guid GmUserId,
    List<Guid> UserIds,
    List<AiUsageRecord> Records);

/// <summary>
/// Custom FsCheck arbitraries for grouping partition property tests.
/// Generates AiUsageRecords with multiple distinct operation types, models, and users.
/// </summary>
public class GroupingScenarioArbitraries
{
    private static readonly AiOperationType[] OperationTypes =
    [
        AiOperationType.SourceExtraction,
        AiOperationType.ArtifactSummary,
        AiOperationType.AskLoremaster,
        AiOperationType.SourceExtractionRepair
    ];

    private static readonly string[] Models =
    [
        "gpt-4o",
        "gpt-4o-mini",
        "gpt-3.5-turbo"
    ];

    public static Arbitrary<GroupingScenario> GroupingScenarios()
    {
        var gen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from additionalUserCount in Gen.Choose(1, 4)
            from additionalUserIds in ArbMap.Default.GeneratorFor<Guid>().ArrayOf(additionalUserCount)
            let allUserIds = new[] { gmUserId }.Concat(additionalUserIds).Distinct().ToList()
            from recordCount in Gen.Choose(3, 20)
            from records in GenRecord(worldId, allUserIds).ListOf(recordCount)
            where records.Count > 0
            select new GroupingScenario(worldId, gmUserId, allUserIds, records.ToList());

        return gen.ToArbitrary();
    }

    private static Gen<AiUsageRecord> GenRecord(Guid worldId, List<Guid> userIds)
    {
        return
            from userId in Gen.Elements(userIds.ToArray())
            from operationType in Gen.Elements(OperationTypes)
            from model in Gen.Elements(Models)
            from inputTokens in Gen.Choose(10, 5000)
            from outputTokens in Gen.Choose(10, 3000)
            from costCents in Gen.Choose(1, 500)
            select new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = userId,
                OperationType = operationType,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                EstimatedCostUsd = costCents / 100m,
                DurationMs = 150,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
    }
}
