using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Prep's own monthly pool (feature 29, Requirement 6). Drafting spend counts against it and
/// nothing else does; the daily budget never sees drafting spend, so a month of prep can't
/// stop tonight's extraction, and a busy extraction day can't stop prep.
/// </summary>
[TestFixture]
public class AiBudgetGuardPrepTests
{
    private Guid _worldId;
    private InMemoryAiUsageRecordRepository _usage = null!;
    private InMemoryWorldRepository _worlds = null!;
    private FakeAiPauseGate _pause = null!;
    private AiBudgetOptions _options = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _usage = new InMemoryAiUsageRecordRepository();
        _worlds = new InMemoryWorldRepository();
        _pause = new FakeAiPauseGate();
        _options = new AiBudgetOptions { DailyWorldBudgetUsd = 2.00m, PrepMonthlyBudgetUsd = 10.00m };
    }

    private AiBudgetGuard Guard() => new(_usage, _worlds, Options.Create(_options), _pause);

    private void SeedWorld(decimal? prepMonthlyBudgetUsd = null) =>
        _worlds.CreateAsync(new World
        {
            Id = _worldId,
            Name = "Black Harbor",
            PrepMonthlyBudgetUsd = prepMonthlyBudgetUsd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid(),
        }).GetAwaiter().GetResult();

    private void Spend(decimal usd, AiOperationType op, DateTimeOffset? at = null) =>
        _usage.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = Guid.NewGuid(),
            OperationType = op,
            Model = "gpt-5.4",
            EstimatedCostUsd = usd,
            Succeeded = true,
            CreatedAt = at ?? DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();

    [Test]
    public async Task DraftingSpend_NeverCountsAgainstTheDailyBudget()
    {
        SeedWorld();
        Spend(5.00m, AiOperationType.AdventureDrafting);
        Spend(0.50m, AiOperationType.SourceExtraction);

        var daily = await Guard().GetStatusAsync(_worldId, CancellationToken.None);

        Assert.That(daily.SpentTodayUsd, Is.EqualTo(0.50m));
        Assert.That(daily.IsExceeded, Is.False);
        Assert.That(await Guard().CheckAsync(_worldId, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ASpentDailyBudget_DoesNotBlockPrep()
    {
        SeedWorld();
        Spend(3.00m, AiOperationType.SourceExtraction);

        Assert.That(await Guard().CheckAsync(_worldId, CancellationToken.None), Is.Not.Null, "the daily budget is spent");
        Assert.That(await Guard().CheckPrepAsync(_worldId, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task PrepStatus_CountsOnlyThisMonthsDrafting()
    {
        SeedWorld();
        var now = DateTime.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        Spend(1.25m, AiOperationType.AdventureDrafting);
        Spend(4.00m, AiOperationType.AdventureDrafting, monthStart.AddSeconds(-1));
        Spend(0.75m, AiOperationType.AskLoremaster);

        var status = await Guard().GetPrepStatusAsync(_worldId, CancellationToken.None);

        Assert.That(status.SpentThisMonthUsd, Is.EqualTo(1.25m));
        Assert.That(status.MonthlyBudgetUsd, Is.EqualTo(10.00m), "an unset world inherits the configured default");
    }

    [Test]
    public async Task TheWorldsOwnCap_WinsOverTheDefault()
    {
        SeedWorld(prepMonthlyBudgetUsd: 1.00m);
        Spend(1.00m, AiOperationType.AdventureDrafting);

        var error = await Guard().CheckPrepAsync(_worldId, CancellationToken.None);

        Assert.That(error!.StatusCode, Is.EqualTo(429));
        Assert.That(error.Code, Is.EqualTo("prep_budget_exceeded"));
        Assert.That(error.Message, Does.Contain("$1.00"));
    }

    [Test]
    public async Task AZeroCap_Blocks_AndANullDefault_IsNoCeiling()
    {
        SeedWorld();
        _options.PrepMonthlyBudgetUsd = 0m;
        Assert.That(await Guard().CheckPrepAsync(_worldId, CancellationToken.None), Is.Not.Null, "zero is a ceiling of zero");

        _options.PrepMonthlyBudgetUsd = null;
        Spend(500m, AiOperationType.AdventureDrafting);
        Assert.That(await Guard().CheckPrepAsync(_worldId, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task APause_StopsPrep_BeforeTheBudgetIsRead()
    {
        SeedWorld();
        _pause.Pause("Provider outage");

        var error = await Guard().CheckPrepAsync(_worldId, CancellationToken.None);

        Assert.That(error!.StatusCode, Is.EqualTo(503));
        Assert.That(error.Code, Is.EqualTo("ai_paused"));
    }
}
