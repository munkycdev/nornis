using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Unit tests for CostService date range validation and aggregation behavior.
///
/// **Validates: Requirements 8.2, 8.3, 8.4, 8.5, 8.6**
/// </summary>
[TestFixture]
public class CostServiceDateRangeAndAggregationTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid KeldaUserId = Guid.NewGuid();

    private InMemoryAiUsageRecordRepository _aiUsageRepo = null!;
    private InMemoryWorldMemberRepository _memberRepo = null!;
    private InMemoryWorldRepository _worldRepo = null!;
    private CostService _costService = null!;

    [SetUp]
    public void SetUp()
    {
        _aiUsageRepo = new InMemoryAiUsageRecordRepository();
        _memberRepo = new InMemoryWorldMemberRepository();
        _worldRepo = new InMemoryWorldRepository(_memberRepo);

        _memberRepo.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = KeldaUserId,
            Role = WorldRole.GM,
            DisplayName = "Kelda",
            JoinedAt = DateTimeOffset.UtcNow.AddDays(-30)
        }).GetAwaiter().GetResult();

        _costService = new CostService(
            _aiUsageRepo,
            _memberRepo,
            _worldRepo,
            NullLogger<CostService>.Instance);
    }

    #region startDate after endDate → 400 validation error with invalid_date_range code

    [Test]
    public async Task GetByUserAsync_StartDateAfterEndDate_Returns400WithInvalidDateRangeCode()
    {
        var startDate = new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2024, 6, 10, 0, 0, 0, TimeSpan.Zero);

        var result = await _costService.GetByUserAsync(
            WorldId, KeldaUserId, WorldRole.GM, startDate, endDate, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_date_range"));
    }

    [Test]
    public async Task GetByOperationTypeAsync_StartDateAfterEndDate_Returns400WithInvalidDateRangeCode()
    {
        var startDate = new DateTimeOffset(2024, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2024, 8, 19, 12, 0, 0, TimeSpan.Zero);

        var result = await _costService.GetByOperationTypeAsync(
            WorldId, KeldaUserId, WorldRole.GM, startDate, endDate, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_date_range"));
    }

    [Test]
    public async Task GetByModelAsync_StartDateAfterEndDate_Returns400WithInvalidDateRangeCode()
    {
        var startDate = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = await _costService.GetByModelAsync(
            WorldId, KeldaUserId, WorldRole.GM, startDate, endDate, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_date_range"));
    }

    #endregion

    #region startDate equal to endDate → proceeds normally

    [Test]
    public async Task GetByUserAsync_StartDateEqualsEndDate_ProceedsNormally()
    {
        var sameDate = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: sameDate);

        var result = await _costService.GetByUserAsync(
            WorldId, KeldaUserId, WorldRole.GM, sameDate, sameDate, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
    }

    [Test]
    public async Task GetByOperationTypeAsync_StartDateEqualsEndDate_ProceedsNormally()
    {
        var sameDate = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: sameDate);

        var result = await _costService.GetByOperationTypeAsync(
            WorldId, KeldaUserId, WorldRole.GM, sameDate, sameDate, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
    }

    [Test]
    public async Task GetByModelAsync_StartDateEqualsEndDate_ProceedsNormally()
    {
        var sameDate = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: sameDate);

        var result = await _costService.GetByModelAsync(
            WorldId, KeldaUserId, WorldRole.GM, sameDate, sameDate, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.Not.Null);
    }

    #endregion

    #region Both null → no date restriction

    [Test]
    public async Task GetByUserAsync_BothDatesNull_ReturnsAllRecords()
    {
        SeedRecord(createdAt: DateTimeOffset.UtcNow.AddDays(-100));
        SeedRecord(createdAt: DateTimeOffset.UtcNow.AddDays(-50));
        SeedRecord(createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _costService.GetByUserAsync(
            WorldId, KeldaUserId, WorldRole.GM, null, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(u => u.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(3));
    }

    [Test]
    public async Task GetByOperationTypeAsync_BothDatesNull_ReturnsAllRecords()
    {
        SeedRecord(createdAt: DateTimeOffset.UtcNow.AddDays(-200));
        SeedRecord(createdAt: DateTimeOffset.UtcNow.AddDays(-10));

        var result = await _costService.GetByOperationTypeAsync(
            WorldId, KeldaUserId, WorldRole.GM, null, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByModelAsync_BothDatesNull_ReturnsAllRecords()
    {
        SeedRecord(createdAt: DateTimeOffset.UtcNow.AddDays(-365));
        SeedRecord(createdAt: DateTimeOffset.UtcNow);

        var result = await _costService.GetByModelAsync(
            WorldId, KeldaUserId, WorldRole.GM, null, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    #endregion

    #region Only startDate provided → filters from startDate

    [Test]
    public async Task GetByUserAsync_OnlyStartDateProvided_FiltersFromStartDate()
    {
        var cutoff = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: new DateTimeOffset(2024, 5, 15, 0, 0, 0, TimeSpan.Zero)); // before
        SeedRecord(createdAt: new DateTimeOffset(2024, 6, 10, 0, 0, 0, TimeSpan.Zero)); // after
        SeedRecord(createdAt: new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));  // after

        var result = await _costService.GetByUserAsync(
            WorldId, KeldaUserId, WorldRole.GM, cutoff, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(u => u.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByOperationTypeAsync_OnlyStartDateProvided_FiltersFromStartDate()
    {
        var cutoff = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: new DateTimeOffset(2024, 2, 28, 23, 59, 59, TimeSpan.Zero)); // before
        SeedRecord(createdAt: new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero));      // on cutoff (included)
        SeedRecord(createdAt: new DateTimeOffset(2024, 4, 15, 0, 0, 0, TimeSpan.Zero));     // after

        var result = await _costService.GetByOperationTypeAsync(
            WorldId, KeldaUserId, WorldRole.GM, cutoff, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByModelAsync_OnlyStartDateProvided_FiltersFromStartDate()
    {
        var cutoff = new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: new DateTimeOffset(2024, 8, 31, 0, 0, 0, TimeSpan.Zero)); // before
        SeedRecord(createdAt: new DateTimeOffset(2024, 9, 1, 0, 0, 0, TimeSpan.Zero));  // on cutoff
        SeedRecord(createdAt: new DateTimeOffset(2024, 10, 1, 0, 0, 0, TimeSpan.Zero)); // after

        var result = await _costService.GetByModelAsync(
            WorldId, KeldaUserId, WorldRole.GM, cutoff, null, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    #endregion

    #region Only endDate provided → filters to endDate

    [Test]
    public async Task GetByUserAsync_OnlyEndDateProvided_FiltersToEndDate()
    {
        var cutoff = new DateTimeOffset(2024, 6, 30, 23, 59, 59, TimeSpan.Zero);
        SeedRecord(createdAt: new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));  // before cutoff
        SeedRecord(createdAt: new DateTimeOffset(2024, 6, 15, 0, 0, 0, TimeSpan.Zero)); // before cutoff
        SeedRecord(createdAt: new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero));  // after cutoff

        var result = await _costService.GetByUserAsync(
            WorldId, KeldaUserId, WorldRole.GM, null, cutoff, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(u => u.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByOperationTypeAsync_OnlyEndDateProvided_FiltersToEndDate()
    {
        var cutoff = new DateTimeOffset(2024, 4, 30, 0, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: new DateTimeOffset(2024, 4, 30, 0, 0, 0, TimeSpan.Zero)); // on cutoff (included)
        SeedRecord(createdAt: new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero));  // before
        SeedRecord(createdAt: new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero));  // after

        var result = await _costService.GetByOperationTypeAsync(
            WorldId, KeldaUserId, WorldRole.GM, null, cutoff, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    [Test]
    public async Task GetByModelAsync_OnlyEndDateProvided_FiltersToEndDate()
    {
        var cutoff = new DateTimeOffset(2024, 11, 15, 12, 0, 0, TimeSpan.Zero);
        SeedRecord(createdAt: new DateTimeOffset(2024, 10, 1, 0, 0, 0, TimeSpan.Zero));  // before
        SeedRecord(createdAt: new DateTimeOffset(2024, 11, 15, 12, 0, 0, TimeSpan.Zero)); // on cutoff (included)
        SeedRecord(createdAt: new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero));   // after

        var result = await _costService.GetByModelAsync(
            WorldId, KeldaUserId, WorldRole.GM, null, cutoff, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var totalOps = result.Value!.Sum(r => r.Summary.OperationCount);
        Assert.That(totalOps, Is.EqualTo(2));
    }

    #endregion

    #region Empty world → all CostSummary fields are zero

    [Test]
    public async Task GetSummaryAsync_EmptyWorld_AllCostSummaryFieldsAreZero()
    {
        // No records seeded — world is empty
        var result = await _costService.GetSummaryAsync(
            WorldId, KeldaUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        var summary = result.Value!;
        AssertCostSummaryIsZero(summary.Today, "Today");
        AssertCostSummaryIsZero(summary.ThisWeek, "ThisWeek");
        AssertCostSummaryIsZero(summary.ThisMonth, "ThisMonth");
        AssertCostSummaryIsZero(summary.AllTime, "AllTime");
    }

    #endregion

    #region Helpers

    private void SeedRecord(
        DateTimeOffset createdAt,
        Guid? userId = null,
        int inputTokens = 100,
        int outputTokens = 50)
    {
        _aiUsageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            UserId = userId ?? KeldaUserId,
            OperationType = AiOperationType.SourceExtraction,
            Model = "gpt-4o",
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            EstimatedCostUsd = (inputTokens * 0.005m + outputTokens * 0.015m) / 1000m,
            DurationMs = 200,
            Succeeded = true,
            CreatedAt = createdAt
        }).GetAwaiter().GetResult();
    }

    private static void AssertCostSummaryIsZero(CostSummary summary, string periodName)
    {
        Assert.That(summary.TotalInputTokens, Is.EqualTo(0),
            $"{periodName}.TotalInputTokens should be zero");
        Assert.That(summary.TotalOutputTokens, Is.EqualTo(0),
            $"{periodName}.TotalOutputTokens should be zero");
        Assert.That(summary.TotalTokens, Is.EqualTo(0),
            $"{periodName}.TotalTokens should be zero");
        Assert.That(summary.TotalEstimatedCostUsd, Is.EqualTo(0m),
            $"{periodName}.TotalEstimatedCostUsd should be zero");
        Assert.That(summary.OperationCount, Is.EqualTo(0),
            $"{periodName}.OperationCount should be zero");
    }

    #endregion
}
