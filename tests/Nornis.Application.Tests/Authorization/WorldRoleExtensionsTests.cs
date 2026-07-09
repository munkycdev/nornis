using Nornis.Application.Authorization;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Authorization;

[TestFixture]
public class WorldRoleExtensionsTests
{
    [Test]
    public void Rank_GM_Returns3()
    {
        Assert.That(WorldRole.GM.Rank(), Is.EqualTo(3));
    }

    [Test]
    public void Rank_Player_Returns2()
    {
        Assert.That(WorldRole.Player.Rank(), Is.EqualTo(2));
    }

    [Test]
    public void Rank_Observer_Returns1()
    {
        Assert.That(WorldRole.Observer.Rank(), Is.EqualTo(1));
    }

    [Test]
    public void Rank_UndefinedValue_Returns0()
    {
        var undefined = (WorldRole)999;
        Assert.That(undefined.Rank(), Is.EqualTo(0));
    }

    [Test]
    public void Rank_PreservesHierarchy_GM_GreaterThan_Player_GreaterThan_Observer()
    {
        Assert.That(WorldRole.GM.Rank(), Is.GreaterThan(WorldRole.Player.Rank()));
        Assert.That(WorldRole.Player.Rank(), Is.GreaterThan(WorldRole.Observer.Rank()));
    }

    [Test]
    public void IsAtLeast_GM_MeetsGM()
    {
        Assert.That(WorldRole.GM.IsAtLeast(WorldRole.GM), Is.True);
    }

    [Test]
    public void IsAtLeast_GM_MeetsPlayer()
    {
        Assert.That(WorldRole.GM.IsAtLeast(WorldRole.Player), Is.True);
    }

    [Test]
    public void IsAtLeast_GM_MeetsObserver()
    {
        Assert.That(WorldRole.GM.IsAtLeast(WorldRole.Observer), Is.True);
    }

    [Test]
    public void IsAtLeast_Player_DoesNotMeetGM()
    {
        Assert.That(WorldRole.Player.IsAtLeast(WorldRole.GM), Is.False);
    }

    [Test]
    public void IsAtLeast_Player_MeetsPlayer()
    {
        Assert.That(WorldRole.Player.IsAtLeast(WorldRole.Player), Is.True);
    }

    [Test]
    public void IsAtLeast_Player_MeetsObserver()
    {
        Assert.That(WorldRole.Player.IsAtLeast(WorldRole.Observer), Is.True);
    }

    [Test]
    public void IsAtLeast_Observer_DoesNotMeetGM()
    {
        Assert.That(WorldRole.Observer.IsAtLeast(WorldRole.GM), Is.False);
    }

    [Test]
    public void IsAtLeast_Observer_DoesNotMeetPlayer()
    {
        Assert.That(WorldRole.Observer.IsAtLeast(WorldRole.Player), Is.False);
    }

    [Test]
    public void IsAtLeast_Observer_MeetsObserver()
    {
        Assert.That(WorldRole.Observer.IsAtLeast(WorldRole.Observer), Is.True);
    }
}
