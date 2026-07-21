using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The monthly cap that gates anonymous public "Ask the Loremaster". The cap is also the
/// on/off switch (null or ≤ 0 means off), and only anonymous ask spend — AskLoremaster rows
/// with no user, this calendar month — counts against it.
/// </summary>
[TestFixture]
public class AiBudgetGuardPublicAskTests
{
    private Guid _worldId;
    private InMemoryAiUsageRecordRepository _usageRepo = null!;
    private InMemoryWorldRepository _worldRepo = null!;

    [SetUp]
    public void SetUp()
    {
        _worldId = Guid.NewGuid();
        _usageRepo = new InMemoryAiUsageRecordRepository();
        _worldRepo = new InMemoryWorldRepository();
    }

    private AiBudgetGuard Guard() =>
        new(_usageRepo, _worldRepo, Options.Create(new AiBudgetOptions()));

    private void SeedWorld(decimal? publicAskMonthlyBudgetUsd)
    {
        _worldRepo.CreateAsync(new World
        {
            Id = _worldId,
            Name = "Test World",
            PublicAskMonthlyBudgetUsd = publicAskMonthlyBudgetUsd,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = Guid.NewGuid()
        }).GetAwaiter().GetResult();
    }

    private void SeedUsage(
        decimal costUsd, Guid? userId,
        AiOperationType op = AiOperationType.AskLoremaster, DateTimeOffset? createdAt = null)
    {
        _usageRepo.CreateAsync(new AiUsageRecord
        {
            Id = Guid.NewGuid(),
            WorldId = _worldId,
            UserId = userId,
            OperationType = op,
            Model = "gpt-4o",
            EstimatedCostUsd = costUsd,
            Succeeded = true,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();
    }

    private static DateTimeOffset ThisMonthStartUtc()
    {
        var now = DateTime.UtcNow;
        return new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [Test]
    public async Task NoBudgetSet_PublicAskDisabled()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: null);

        var status = await Guard().GetPublicAskStatusAsync(_worldId, CancellationToken.None);
        var error = await Guard().CheckPublicAskAsync(_worldId, CancellationToken.None);

        Assert.That(status.IsEnabled, Is.False);
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.StatusCode, Is.EqualTo(404));
        Assert.That(error.Code, Is.EqualTo("public_ask_unavailable"));
    }

    [Test]
    public async Task ZeroBudget_PublicAskDisabled()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: 0m);

        var status = await Guard().GetPublicAskStatusAsync(_worldId, CancellationToken.None);

        Assert.That(status.IsEnabled, Is.False);
    }

    [Test]
    public async Task UnderMonthlyCap_Allows_AndReportsSpend()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: 10m);
        SeedUsage(2.00m, userId: null); // an anonymous public ask

        var status = await Guard().GetPublicAskStatusAsync(_worldId, CancellationToken.None);
        var error = await Guard().CheckPublicAskAsync(_worldId, CancellationToken.None);

        Assert.That(status.IsEnabled, Is.True);
        Assert.That(status.SpentThisMonthUsd, Is.EqualTo(2.00m));
        Assert.That(status.IsExceeded, Is.False);
        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task AtOrOverMonthlyCap_Blocks()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: 10m);
        SeedUsage(6.00m, userId: null);
        SeedUsage(5.00m, userId: null);

        var error = await Guard().CheckPublicAskAsync(_worldId, CancellationToken.None);

        Assert.That(error, Is.Not.Null);
        Assert.That(error!.StatusCode, Is.EqualTo(429));
        Assert.That(error.Code, Is.EqualTo("public_ask_budget_exceeded"));
    }

    [Test]
    public async Task MemberAskSpend_DoesNotCountAgainstPublicCap()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: 10m);
        SeedUsage(20.00m, userId: Guid.NewGuid()); // a member's own ask carries a user

        var status = await Guard().GetPublicAskStatusAsync(_worldId, CancellationToken.None);
        var error = await Guard().CheckPublicAskAsync(_worldId, CancellationToken.None);

        Assert.That(status.SpentThisMonthUsd, Is.EqualTo(0m));
        Assert.That(error, Is.Null);
    }

    [Test]
    public async Task NonAskSpend_DoesNotCountAgainstPublicCap()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: 10m);
        SeedUsage(20.00m, userId: null, op: AiOperationType.SourceExtraction);

        var status = await Guard().GetPublicAskStatusAsync(_worldId, CancellationToken.None);

        Assert.That(status.SpentThisMonthUsd, Is.EqualTo(0m));
    }

    [Test]
    public async Task LastMonthSpend_DoesNotCount()
    {
        SeedWorld(publicAskMonthlyBudgetUsd: 10m);
        SeedUsage(20.00m, userId: null, createdAt: ThisMonthStartUtc().AddDays(-1));

        var status = await Guard().GetPublicAskStatusAsync(_worldId, CancellationToken.None);
        var error = await Guard().CheckPublicAskAsync(_worldId, CancellationToken.None);

        Assert.That(status.SpentThisMonthUsd, Is.EqualTo(0m));
        Assert.That(error, Is.Null);
    }
}
