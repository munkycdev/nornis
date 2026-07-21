using System.Globalization;
using NUnit.Framework;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Validation and application of the per-world public Ask monthly budget in world settings.
/// Mirrors the daily-budget pattern: a value in range sets it, out-of-range is rejected, and
/// the clear flag disables public Ask.
/// </summary>
[TestFixture]
public class WorldServicePublicAskBudgetTests
{
    private InMemoryWorldRepository _worlds = null!;
    private InMemoryWorldMemberRepository _members = null!;
    private WorldService _sut = null!;
    private World _world = null!;
    private Guid _gmId;

    [SetUp]
    public void SetUp()
    {
        _worlds = new InMemoryWorldRepository();
        _members = new InMemoryWorldMemberRepository();
        _sut = new WorldService(_worlds, _members);
        _gmId = Guid.NewGuid();

        _world = new World
        {
            Id = Guid.NewGuid(),
            Name = "Symbaroum",
            CreatedByUserId = _gmId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _worlds.CreateAsync(_world).GetAwaiter().GetResult();
        _members.CreateAsync(new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = _world.Id,
            UserId = _gmId,
            Role = WorldRole.GM,
            JoinedAt = DateTimeOffset.UtcNow,
        }).GetAwaiter().GetResult();
    }

    private UpdateWorldCommand Command(decimal? budget = null, bool clear = false) =>
        new(_world.Id, null, null, null, _gmId,
            PublicAskMonthlyBudgetUsd: budget, ClearPublicAskBudget: clear);

    [Test]
    public async Task Update_SetsMonthlyBudget()
    {
        var result = await _sut.UpdateAsync(Command(budget: 15m), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PublicAskMonthlyBudgetUsd, Is.EqualTo(15m));
    }

    [TestCase("0.005")]
    [TestCase("0")]
    [TestCase("-1")]
    [TestCase("1000.01")]
    public async Task Update_OutOfRangeBudget_Returns400(string budgetStr)
    {
        var budget = decimal.Parse(budgetStr, CultureInfo.InvariantCulture);

        var result = await _sut.UpdateAsync(Command(budget: budget), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task Update_ClearBudget_DisablesPublicAsk()
    {
        _world.PublicAskMonthlyBudgetUsd = 20m;
        await _worlds.UpdateAsync(_world);

        var result = await _sut.UpdateAsync(Command(clear: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PublicAskMonthlyBudgetUsd, Is.Null);
    }

    [Test]
    public async Task Update_ExplicitBudget_WinsOverClearFlag()
    {
        var result = await _sut.UpdateAsync(
            new UpdateWorldCommand(_world.Id, null, null, null, _gmId,
                PublicAskMonthlyBudgetUsd: 5m, ClearPublicAskBudget: true),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PublicAskMonthlyBudgetUsd, Is.EqualTo(5m),
            "an explicit value takes precedence over the clear flag");
    }
}
