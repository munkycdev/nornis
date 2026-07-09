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
/// Property 5: Date Range Validation
///
/// For any pair of DateTimeOffset values where startDate is strictly after endDate, the CostService
/// SHALL return a validation error (HTTP 400) with error code "invalid_date_range". For any pair
/// where startDate is before or equal to endDate, the service SHALL proceed with aggregation
/// successfully.
///
/// **Validates: Requirements 8.4, 8.5**
/// </summary>
[TestFixture]
[Category("Feature: cost-dashboard, Property 5: Date range validation")]
public class DateRangeValidationTests
{
    private InMemoryAiUsageRecordRepository _aiUsageRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private InMemoryWorldRepository _worldRepo = null!;
    private CostService _costService = null!;
    private Guid _worldId;
    private Guid _userId;

    [SetUp]
    public void SetUp()
    {
        _aiUsageRepo = new InMemoryAiUsageRecordRepository();
        _memberRepo = new InMemoryWorldMemberRepository();
        _worldRepo = new InMemoryWorldRepository();
        _costService = new CostService(
            _aiUsageRepo,
            _memberRepo,
            _worldRepo,
            NullLogger<CostService>.Instance);

        _worldId = Guid.NewGuid();
        _userId = Guid.NewGuid();

        // Seed a world member so username resolution works
        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = _userId,
            Role = WorldRole.GM,
            DisplayName = "Kelda",
            JoinedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();

        // Seed at least one record so the success case has data to aggregate
        _aiUsageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = _userId,
            OperationType = AiOperationType.AskLoremaster,
            Model = "gpt-4o",
            InputTokens = 100,
            OutputTokens = 50,
            TotalTokens = 150,
            EstimatedCostUsd = 0.01m,
            DurationMs = 200,
            Succeeded = true,
            CreatedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidDateRangeArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 5: Date range validation")]
    public void GetByUserAsync_InvalidDateRange_ReturnsValidationError(InvalidDateRangePair pair)
    {
        // Act
        var result = _costService.GetByUserAsync(
            _worldId, _userId, WorldRole.GM,
            pair.StartDate, pair.EndDate, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Should fail when startDate > endDate.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_date_range"));
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidDateRangeArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 5: Date range validation")]
    public void GetByOperationTypeAsync_InvalidDateRange_ReturnsValidationError(InvalidDateRangePair pair)
    {
        // Act
        var result = _costService.GetByOperationTypeAsync(
            _worldId, _userId, WorldRole.GM,
            pair.StartDate, pair.EndDate, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Should fail when startDate > endDate.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_date_range"));
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(InvalidDateRangeArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 5: Date range validation")]
    public void GetByModelAsync_InvalidDateRange_ReturnsValidationError(InvalidDateRangePair pair)
    {
        // Act
        var result = _costService.GetByModelAsync(
            _worldId, _userId, WorldRole.GM,
            pair.StartDate, pair.EndDate, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.False, "Should fail when startDate > endDate.");
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_date_range"));
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ValidDateRangeArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 5: Date range validation")]
    public void GetByUserAsync_ValidDateRange_ProceedsSuccessfully(ValidDateRangePair pair)
    {
        // Act
        var result = _costService.GetByUserAsync(
            _worldId, _userId, WorldRole.GM,
            pair.StartDate, pair.EndDate, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True,
            "Should succeed when startDate <= endDate.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ValidDateRangeArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 5: Date range validation")]
    public void GetByOperationTypeAsync_ValidDateRange_ProceedsSuccessfully(ValidDateRangePair pair)
    {
        // Act
        var result = _costService.GetByOperationTypeAsync(
            _worldId, _userId, WorldRole.GM,
            pair.StartDate, pair.EndDate, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True,
            "Should succeed when startDate <= endDate.");
    }

    [FsCheck.NUnit.Property(
        Arbitrary = [typeof(ValidDateRangeArbitraries)],
        MaxTest = 100)]
    [Description("Feature: cost-dashboard, Property 5: Date range validation")]
    public void GetByModelAsync_ValidDateRange_ProceedsSuccessfully(ValidDateRangePair pair)
    {
        // Act
        var result = _costService.GetByModelAsync(
            _worldId, _userId, WorldRole.GM,
            pair.StartDate, pair.EndDate, CancellationToken.None)
            .GetAwaiter().GetResult();

        // Assert
        Assert.That(result.IsSuccess, Is.True,
            "Should succeed when startDate <= endDate.");
    }
}

/// <summary>
/// A pair of DateTimeOffset values where StartDate is strictly after EndDate.
/// </summary>
public record InvalidDateRangePair(DateTimeOffset StartDate, DateTimeOffset EndDate);

/// <summary>
/// A pair of DateTimeOffset values where StartDate is before or equal to EndDate.
/// </summary>
public record ValidDateRangePair(DateTimeOffset StartDate, DateTimeOffset EndDate);

/// <summary>
/// Generates pairs where startDate > endDate (invalid ranges).
/// </summary>
public class InvalidDateRangeArbitraries
{
    public static Arbitrary<InvalidDateRangePair> InvalidDateRangePairs()
    {
        var gen =
            from year in Gen.Choose(2020, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from offsetMinutes in Gen.Choose(1, 525600) // 1 minute to ~1 year
            let endDate = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
            let startDate = endDate.AddMinutes(offsetMinutes) // startDate > endDate
            select new InvalidDateRangePair(startDate, endDate);

        return gen.ToArbitrary();
    }
}

/// <summary>
/// Generates pairs where startDate <= endDate (valid ranges).
/// </summary>
public class ValidDateRangeArbitraries
{
    public static Arbitrary<ValidDateRangePair> ValidDateRangePairs()
    {
        var gen = Gen.OneOf(EqualDatesGen(), StartBeforeEndGen());
        return gen.ToArbitrary();
    }

    private static Gen<ValidDateRangePair> EqualDatesGen()
    {
        return
            from year in Gen.Choose(2020, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            let date = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
            select new ValidDateRangePair(date, date);
    }

    private static Gen<ValidDateRangePair> StartBeforeEndGen()
    {
        return
            from year in Gen.Choose(2020, 2030)
            from month in Gen.Choose(1, 12)
            from day in Gen.Choose(1, 28)
            from hour in Gen.Choose(0, 23)
            from minute in Gen.Choose(0, 59)
            from offsetMinutes in Gen.Choose(1, 525600) // 1 minute to ~1 year
            let startDate = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero)
            let endDate = startDate.AddMinutes(offsetMinutes) // endDate > startDate
            select new ValidDateRangePair(startDate, endDate);
    }
}
