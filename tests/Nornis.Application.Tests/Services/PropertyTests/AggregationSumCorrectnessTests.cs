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
/// Property 2: Aggregation Sum Correctness
///
/// For any non-empty set of AiUsageRecords matching filters, the resulting CostSummary SHALL have
/// TotalInputTokens equal to the sum of all matching records' InputTokens, TotalOutputTokens equal
/// to the sum of OutputTokens, TotalTokens equal to the sum of TotalTokens, TotalEstimatedCostUsd
/// equal to the sum of EstimatedCostUsd, and OperationCount equal to the count of matching records.
///
/// **Validates: Requirements 3.6, 9.2, 9.3, 10.2**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 2: Aggregation sum correctness")]
public class AggregationSumCorrectnessTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(AggregationSumArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 2: Aggregation sum correctness")]
    public void GetSummaryAsync_AllTime_MatchesLinearSumOfRecords(AggregationSumInput input)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();
        var logger = NullLogger<CostService>.Instance;

        var service = new CostService(aiUsageRepo, memberRepo, worldRepo, logger);

        // Seed records into the in-memory repository
        foreach (var record in input.Records)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act - Call GetSummaryAsync as GM (userId filter = null, sees all records)
        var result = service.GetSummaryAsync(
            input.WorldId,
            input.GmUserId,
            WorldRole.GM,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should succeed
        Assert.That(result.IsSuccess, Is.True, "GetSummaryAsync should succeed.");

        var allTime = result.Value!.AllTime;

        // Compute expected sums linearly
        var expectedInputTokens = input.Records.Sum(r => (long)r.InputTokens);
        var expectedOutputTokens = input.Records.Sum(r => (long)r.OutputTokens);
        var expectedTotalTokens = input.Records.Sum(r => (long)r.TotalTokens);
        var expectedCost = input.Records.Sum(r => r.EstimatedCostUsd);
        var expectedCount = input.Records.Count;

        // Assert TotalInputTokens = sum of InputTokens
        Assert.That(allTime.TotalInputTokens, Is.EqualTo(expectedInputTokens),
            "TotalInputTokens must equal the sum of all records' InputTokens.");

        // Assert TotalOutputTokens = sum of OutputTokens
        Assert.That(allTime.TotalOutputTokens, Is.EqualTo(expectedOutputTokens),
            "TotalOutputTokens must equal the sum of all records' OutputTokens.");

        // Assert TotalTokens = sum of TotalTokens
        Assert.That(allTime.TotalTokens, Is.EqualTo(expectedTotalTokens),
            "TotalTokens must equal the sum of all records' TotalTokens.");

        // Assert TotalEstimatedCostUsd = sum of EstimatedCostUsd
        Assert.That(allTime.TotalEstimatedCostUsd, Is.EqualTo(expectedCost),
            "TotalEstimatedCostUsd must equal the sum of all records' EstimatedCostUsd.");

        // Assert OperationCount = count of records
        Assert.That(allTime.OperationCount, Is.EqualTo(expectedCount),
            "OperationCount must equal the count of matching records.");
    }
}

/// <summary>
/// Input model for aggregation sum correctness property tests.
/// </summary>
public record AggregationSumInput(
    Guid WorldId,
    Guid GmUserId,
    List<AiUsageRecord> Records);

/// <summary>
/// Custom FsCheck arbitraries for aggregation sum correctness tests.
/// </summary>
public class AggregationSumArbitraries
{
    private static readonly string[] Models = ["gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo", "claude-3-opus"];

    public static Arbitrary<AggregationSumInput> AggregationSumInputs()
    {
        var inputGen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from recordCount in Gen.Choose(1, 20)
            from records in GenRecords(worldId, recordCount)
            select new AggregationSumInput(worldId, gmUserId, records);

        return inputGen.ToArbitrary();
    }

    private static Gen<List<AiUsageRecord>> GenRecords(Guid worldId, int count)
    {
        return GenRecord(worldId).ArrayOf(count)
            .Select(records => records.ToList());
    }

    private static Gen<AiUsageRecord> GenRecord(Guid worldId)
    {
        return
            from id in ArbMap.Default.GeneratorFor<Guid>()
            from userId in ArbMap.Default.GeneratorFor<Guid>()
            from operationType in Gen.Elements(
                AiOperationType.SourceExtraction,
                AiOperationType.ArtifactSummary,
                AiOperationType.AskLoremaster,
                AiOperationType.SourceExtractionRepair)
            from model in Gen.Elements(Models)
            from inputTokens in Gen.Choose(0, 100_000)
            from outputTokens in Gen.Choose(0, 50_000)
            from costCents in Gen.Choose(0, 10_000)
            from durationMs in Gen.Choose(100, 30_000)
            select new AiUsageRecord
            {
                Id = id,
                WorldId = worldId,
                UserId = userId,
                OperationType = operationType,
                Model = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                TotalTokens = inputTokens + outputTokens,
                EstimatedCostUsd = costCents / 100m,
                DurationMs = durationMs,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-7)
            };
    }
}
