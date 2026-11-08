using Nornis.Application.Services;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>Every AI operation spends from exactly one pool, and only Prep's own operations spend from its pool.</summary>
[TestFixture]
public class AiSpendPoolsTests
{
    [Test]
    public void EveryOperation_HasAPool() =>
        Assert.That(() => Enum.GetValues<AiOperationType>().Select(AiSpendPools.PoolOf).ToList(), Throws.Nothing);

    [Test]
    public void ThePools_SplitTheOperationsBetweenThem()
    {
        var daily = AiSpendPools.OperationsIn(AiSpendPool.DailyWorld);
        var prep = AiSpendPools.OperationsIn(AiSpendPool.PrepMonthly);

        Assert.That(prep, Is.EqualTo([AiOperationType.AdventureDrafting, AiOperationType.AdventureConversation,
            AiOperationType.CoverArtBrief, AiOperationType.CoverArt, AiOperationType.KeyArt]),
            "cover art (feature 38) and key art (feature 42) are Prep's spending too");
        Assert.That(daily.Concat(prep), Is.EquivalentTo(Enum.GetValues<AiOperationType>()));
        Assert.That(daily, Does.Not.Contain(AiOperationType.AdventureDrafting));
        Assert.That(daily, Does.Not.Contain(AiOperationType.AdventureConversation));
    }
}
