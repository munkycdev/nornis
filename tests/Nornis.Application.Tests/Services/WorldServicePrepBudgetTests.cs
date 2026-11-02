using System.Globalization;
using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The per-world Prep monthly budget in world settings (feature 29). Validated like the public
/// Ask cap: $0.01 to $1000, and the clear flag returns the world to the configured default.
/// </summary>
[TestFixture]
public class WorldServicePrepBudgetTests
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
        _sut = new WorldService(_worlds, _members, Options.Create(new DemoWorldOptions()));
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
        new(_world.Id, null, null, null, _gmId, WorldRole.GM,
            PrepMonthlyBudgetUsd: budget, ClearPrepBudget: clear);

    [Test]
    public async Task Update_SetsMonthlyBudget()
    {
        var result = await _sut.UpdateAsync(Command(budget: 15m), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PrepMonthlyBudgetUsd, Is.EqualTo(15m));
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
    public async Task Update_ClearBudget_ReturnsToTheDefault()
    {
        _world.PrepMonthlyBudgetUsd = 20m;
        await _worlds.UpdateAsync(_world);

        var result = await _sut.UpdateAsync(Command(clear: true), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PrepMonthlyBudgetUsd, Is.Null);
    }

    [Test]
    public async Task Update_ExplicitBudget_WinsOverClearFlag()
    {
        var result = await _sut.UpdateAsync(
            new UpdateWorldCommand(_world.Id, null, null, null, _gmId, WorldRole.GM,
                PrepMonthlyBudgetUsd: 5m, ClearPrepBudget: true),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.PrepMonthlyBudgetUsd, Is.EqualTo(5m),
            "an explicit value takes precedence over the clear flag");
    }
}
