using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class AiBudgetGuardTests
{
    private Guid _worldId;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _usageRepo = new InMemoryAiUsageRecordRepository();
    }

    private AiBudgetGuard MakeGuard(decimal dailyBudgetUsd) =>
        new(_usageRepo, Options.Create(new AiBudgetOptions { DailyWorldBudgetUsd = dailyBudgetUsd }));

    private void SeedUsage(decimal costUsd, DateTimeOffset createdAt, Guid? worldId = null)
    {
        _usageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? _worldId,
            OperationType = AiOperationType.AskLoremaster,
            Model = "gpt-4o",
            InputTokens = 100,
            OutputTokens = 100,
            TotalTokens = 200,
            EstimatedCostUsd = costUsd,
            DurationMs = 100,
            Succeeded = true,
            CreatedAt = createdAt
        }).GetAwaiter().GetResult();
    }

    [Test]
    public async Task UnderBudget_Allows()
    {
        SeedUsage(0.50m, DateTimeOffset.UtcNow);

        var error = await MakeGuard(2.00m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task AtOrOverBudget_Blocks()
    {
        SeedUsage(1.50m, DateTimeOffset.UtcNow);
        SeedUsage(0.50m, DateTimeOffset.UtcNow);

        var error = await MakeGuard(2.00m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.StatusCode, Is.EqualTo(429));
        Assert.That(error.Code, Is.EqualTo("ai_budget_exceeded"));
    }

    [Test]
    public async Task YesterdaysSpend_DoesNotCount()
    {
        SeedUsage(10.00m, DateTimeOffset.UtcNow.AddDays(-1).AddHours(-1));

        var error = await MakeGuard(2.00m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task OtherWorldsSpend_DoesNotCount()
    {
        SeedUsage(10.00m, DateTimeOffset.UtcNow, worldId: Guid.NewGuid());

        var error = await MakeGuard(2.00m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task ZeroBudget_DisablesGuard()
    {
        SeedUsage(100.00m, DateTimeOffset.UtcNow);

        var error = await MakeGuard(0m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task Status_ReportsSpendAndBudget()
    {
        SeedUsage(0.75m, DateTimeOffset.UtcNow);

        var status = await MakeGuard(2.00m).GetStatusAsync(_worldId, CancellationToken.None);

        Assert.That(status.SpentTodayUsd, Is.EqualTo(0.75m));
        Assert.That(status.DailyBudgetUsd, Is.EqualTo(2.00m));
        Assert.That(status.IsExceeded, Is.False);
    }
}
