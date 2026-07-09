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
/// Property 3: Date Range Filtering Correctness
///
/// For any set of AiUsageRecords and any date range [startDate, endDate], the aggregation SHALL
/// include exactly those records where CreatedAt >= startDate AND CreatedAt <= endDate. Records
/// outside the range SHALL be excluded.
///
/// **Validates: Requirements 3.2, 3.3, 3.4, 3.5, 5.4, 6.4, 7.4, 8.2, 8.3**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 3: Date range filtering correctness")]
public class DateRangeFilteringCorrectnessTests
{
    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(DateRangeFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 3: Date range filtering correctness")]
    public void GetByOperationTypeAsync_WithDateRange_IncludesExactlyRecordsWithinRange(DateRangeFilteringInput input)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();
        var logger = NullLogger<CostService>.Instance;

        var service = new CostService(aiUsageRepo, memberRepo, worldRepo, logger);

        // Seed all records into the in-memory repository
        foreach (var record in input.Records)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act - Call GetByOperationTypeAsync as GM with the date range
        var result = service.GetByOperationTypeAsync(
            input.WorldId,
            input.GmUserId,
            WorldRole.GM,
            input.StartDate,
            input.EndDate,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert - operation should succeed
        Assert.That(result.IsSuccess, Is.True, "GetByOperationTypeAsync should succeed with valid date range.");

        // Compute expected count: records where CreatedAt >= startDate AND CreatedAt <= endDate
        var expectedRecords = input.Records
            .Where(r => r.CreatedAt >= input.StartDate && r.CreatedAt <= input.EndDate)
            .ToList();

        var expectedOperationCount = expectedRecords.Count;

        // The total OperationCount across all groups should match expected
        var actualOperationCount = result.Value!.Sum(g => g.Summary.OperationCount);

        Assert.That(actualOperationCount, Is.EqualTo(expectedOperationCount),
            $"Total OperationCount across all groups should equal the count of records within [{input.StartDate:O}, {input.EndDate:O}].");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(DateRangeFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 3: Date range filtering correctness")]
    public void GetByModelAsync_WithDateRange_IncludesExactlyRecordsWithinRange(DateRangeFilteringInput input)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();
        var logger = NullLogger<CostService>.Instance;

        var service = new CostService(aiUsageRepo, memberRepo, worldRepo, logger);

        foreach (var record in input.Records)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act
        var result = service.GetByModelAsync(
            input.WorldId,
            input.GmUserId,
            WorldRole.GM,
            input.StartDate,
            input.EndDate,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByModelAsync should succeed with valid date range.");

        var expectedRecords = input.Records
            .Where(r => r.CreatedAt >= input.StartDate && r.CreatedAt <= input.EndDate)
            .ToList();

        var expectedOperationCount = expectedRecords.Count;
        var actualOperationCount = result.Value!.Sum(g => g.Summary.OperationCount);

        Assert.That(actualOperationCount, Is.EqualTo(expectedOperationCount),
            $"Total OperationCount across all model groups should equal the count of records within the date range.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(DateRangeFilteringArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 3: Date range filtering correctness")]
    public void GetByUserAsync_WithDateRange_IncludesExactlyRecordsWithinRange(DateRangeFilteringInput input)
    {
        // Arrange
        var aiUsageRepo = new InMemoryAiUsageRecordRepository();
        var memberRepo = new InMemoryWorldMemberRepository();
        var worldRepo = new InMemoryWorldRepository();
        var logger = NullLogger<CostService>.Instance;

        var service = new CostService(aiUsageRepo, memberRepo, worldRepo, logger);

        // Seed a world member so username resolution works
        memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = input.WorldId,
            UserId = input.GmUserId,
            Role = WorldRole.GM,
            DisplayName = "Kelda",
            JoinedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();

        foreach (var record in input.Records)
        {
            aiUsageRepo.CreateAsync(record, CancellationToken.None).GetAwaiter().GetResult();
        }

        // Act
        var result = service.GetByUserAsync(
            input.WorldId,
            input.GmUserId,
            WorldRole.GM,
            input.StartDate,
            input.EndDate,
            CancellationToken.None).GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True, "GetByUserAsync should succeed with valid date range.");

        var expectedRecords = input.Records
            .Where(r => r.CreatedAt >= input.StartDate && r.CreatedAt <= input.EndDate)
            .ToList();

        var expectedOperationCount = expectedRecords.Count;
        var actualOperationCount = result.Value!.Sum(g => g.Summary.OperationCount);

        Assert.That(actualOperationCount, Is.EqualTo(expectedOperationCount),
            $"Total OperationCount across all user groups should equal the count of records within the date range.");
    }
}

/// <summary>
/// Input model for date range filtering correctness property tests.
/// </summary>
public record DateRangeFilteringInput(
    Guid WorldId,
    Guid GmUserId,
    DateTimeOffset StartDate,
    DateTimeOffset EndDate,
    List<AiUsageRecord> Records);

/// <summary>
/// Custom FsCheck arbitraries for date range filtering correctness tests.
/// Generates records with CreatedAt values spread across a wide date range (2020-2030),
/// then picks a random [start, end] sub-range to filter by.
/// </summary>
public class DateRangeFilteringArbitraries
{
    private static readonly string[] Models = ["gpt-4o", "gpt-4o-mini", "gpt-3.5-turbo"];

    public static Arbitrary<DateRangeFilteringInput> DateRangeFilteringInputs()
    {
        var inputGen =
            from worldId in ArbMap.Default.GeneratorFor<Guid>()
            from gmUserId in ArbMap.Default.GeneratorFor<Guid>()
            from recordCount in Gen.Choose(1, 25)
            from records in GenRecords(worldId, gmUserId, recordCount)
            from dateRange in GenDateRange()
            select new DateRangeFilteringInput(
                worldId,
                gmUserId,
                dateRange.Start,
                dateRange.End,
                records);

        return inputGen.ToArbitrary();
    }

    private static Gen<List<AiUsageRecord>> GenRecords(Guid worldId, Guid gmUserId, int count)
    {
        return GenRecord(worldId, gmUserId).ArrayOf(count)
            .Select(records => records.ToList());
    }

    private static Gen<AiUsageRecord> GenRecord(Guid worldId, Guid gmUserId)
    {
        return
            from id in ArbMap.Default.GeneratorFor<Guid>()
            from useGmUser in ArbMap.Default.GeneratorFor<bool>()
            from otherUserId in ArbMap.Default.GeneratorFor<Guid>()
            from operationType in Gen.Elements(
                AiOperationType.SourceExtraction,
                AiOperationType.ArtifactSummary,
                AiOperationType.AskLoremaster,
                AiOperationType.SourceExtractionRepair)
            from model in Gen.Elements(Models)
            from inputTokens in Gen.Choose(1, 50_000)
            from outputTokens in Gen.Choose(1, 25_000)
            from costCents in Gen.Choose(1, 5_000)
            from durationMs in Gen.Choose(100, 10_000)
            from createdAt in GenReasonableDate()
            let userId = useGmUser ? gmUserId : otherUserId
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
                CreatedAt = createdAt
            };
    }

    /// <summary>
    /// Generates a reasonable DateTimeOffset in the 2020-2030 range.
    /// </summary>
    private static Gen<DateTimeOffset> GenReasonableDate()
    {
        return
            from year in Gen.Choose(2020, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from second in Gen.Choose(0, 59)
            select new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
    }

    /// <summary>
    /// Generates a valid date range [start, end] where start <= end, within 2020-2030.
    /// </summary>
    private static Gen<(DateTimeOffset Start, DateTimeOffset End)> GenDateRange()
    {
        return
            from date1 in GenReasonableDate()
            from date2 in GenReasonableDate()
            let start = date1 <= date2 ? date1 : date2
            let end = date1 <= date2 ? date2 : date1
            select (start, end);
    }
}
