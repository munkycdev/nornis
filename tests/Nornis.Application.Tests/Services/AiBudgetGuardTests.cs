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
    private InMemoryWorldRepository _worldRepo = null!;
    private FakeAiPauseGate _pauseGate = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _worldRepo = new InMemoryWorldRepository();
        _pauseGate = new FakeAiPauseGate();
    }

    private AiBudgetGuard MakeGuard(decimal? dailyBudgetUsd) =>
        new(_usageRepo, _worldRepo, Options.Create(new AiBudgetOptions { DailyWorldBudgetUsd = dailyBudgetUsd }), _pauseGate);

    private void SeedWorld(decimal? dailyAiBudgetUsd)
    {
        _worldRepo.CreateAsync(new Nornis.Domain.Entities.World
        {
            Id = _worldId,
            Name = "Test World",
            DailyAiBudgetUsd = dailyAiBudgetUsd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        }).GetAwaiter().GetResult();
    }

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
    public async Task GetStatusAsync_WorldOverride_WinsOverConfigDefault()
    {
        SeedWorld(dailyAiBudgetUsd: 10m);
        SeedUsage(5m, DateTimeOffset.UtcNow);
        var guard = MakeGuard(dailyBudgetUsd: 2m);

        var status = await guard.GetStatusAsync(_worldId, CancellationToken.None);

        Assert.That(status.DailyBudgetUsd, Is.EqualTo(10m));
        Assert.That(status.IsExceeded, Is.False, "under the world's $10 override despite exceeding the $2 default");
    }

    [Test]
    public async Task GetStatusAsync_WorldWithoutOverride_UsesConfigDefault()
    {
        SeedWorld(dailyAiBudgetUsd: null);
        SeedUsage(3m, DateTimeOffset.UtcNow);
        var guard = MakeGuard(dailyBudgetUsd: 2m);

        var status = await guard.GetStatusAsync(_worldId, CancellationToken.None);

        Assert.That(status.DailyBudgetUsd, Is.EqualTo(2m));
        Assert.That(status.IsExceeded, Is.True);
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
    public async Task ZeroBudget_BlocksEverySpend()
    {
        SeedUsage(100.00m, DateTimeOffset.UtcNow);

        var error = await MakeGuard(0m).CheckAsync(_worldId, CancellationToken.None);

        // Zero used to disable the guard here while meaning "switched off" for the public-Ask
        // cap — the same literal, opposite outcomes. A ceiling of zero is now a ceiling.
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo("ai_budget_exceeded"));
    }

    [Test]
    public async Task ZeroBudget_BlocksBeforeAnythingHasBeenSpent()
    {
        // No usage seeded at all. Nothing spent still exceeds a ceiling of nothing, which is
        // what makes zero a usable "stop" rather than a value that only bites after the fact.
        var error = await MakeGuard(0m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public async Task NoConfiguredBudget_DisablesGuard()
    {
        SeedUsage(100.00m, DateTimeOffset.UtcNow);

        // Absence is now the only way to run without a ceiling, so it has to keep working —
        // this is the escape hatch that zero used to be.
        var error = await MakeGuard(null).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task WorldBudget_OverridesAConfiguredDefaultOfNone()
    {
        SeedWorld(1.00m);
        SeedUsage(2.00m, DateTimeOffset.UtcNow);

        // The world's own ceiling has to win even when the default is "no ceiling", or a world
        // that deliberately set a limit would inherit unlimited spending from configuration.
        var error = await MakeGuard(null).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Not.Null);
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

    [Test]
    public async Task WhenAiIsPaused_EveryPaidCallIsRefused()
    {
        SeedWorld(dailyAiBudgetUsd: 100m);
        _pauseGate.Pause("Provider incident");

        var error = await MakeGuard(100m).CheckAsync(_worldId, CancellationToken.None);

        // Checked here rather than at the eight services that spend money: every paid
        // dispatch already calls CheckAsync, so this one seam reaches all of them.
        Assert.Multiple(() =>
        {
            Assert.That(error, Is.Not.Null);
            Assert.That(error!.StatusCode, Is.EqualTo(503), "a pause is unavailability, not rate limiting");
            Assert.That(error.Code, Is.EqualTo("ai_paused"));
            Assert.That(error.Message, Does.Contain("Provider incident"),
                "the operator's reason is what makes a pause read as deliberate");
        });
    }

    [Test]
    public async Task WhenAiIsPaused_TheBudgetIsNotEvenConsulted()
    {
        // No world seeded, so a budget read would fault or fall through to a default. The
        // pause has to win before any of that: during a provider incident the last thing an
        // operator wants is the switch depending on more of the system still working.
        _pauseGate.Pause();

        var error = await MakeGuard(100m).CheckAsync(_worldId, CancellationToken.None);

        Assert.That(error!.Code, Is.EqualTo("ai_paused"));
    }
}
