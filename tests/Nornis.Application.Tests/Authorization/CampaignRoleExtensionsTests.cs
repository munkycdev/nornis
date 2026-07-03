using Nornis.Application.Authorization;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Authorization;

[TestFixture]
public class CampaignRoleExtensionsTests
{
    [Test]
    public void Rank_GM_Returns3()
    {
        Assert.That(CampaignRole.GM.Rank(), Is.EqualTo(3));
    }

    [Test]
    public void Rank_Player_Returns2()
    {
        Assert.That(CampaignRole.Player.Rank(), Is.EqualTo(2));
    }

    [Test]
    public void Rank_Observer_Returns1()
    {
        Assert.That(CampaignRole.Observer.Rank(), Is.EqualTo(1));
    }

    [Test]
    public void Rank_UndefinedValue_Returns0()
    {
        var undefined = (CampaignRole)999;
        Assert.That(undefined.Rank(), Is.EqualTo(0));
    }

    [Test]
    public void Rank_PreservesHierarchy_GM_GreaterThan_Player_GreaterThan_Observer()
    {
        Assert.That(CampaignRole.GM.Rank(), Is.GreaterThan(CampaignRole.Player.Rank()));
        Assert.That(CampaignRole.Player.Rank(), Is.GreaterThan(CampaignRole.Observer.Rank()));
    }

    [Test]
    public void IsAtLeast_GM_MeetsGM()
    {
        Assert.That(CampaignRole.GM.IsAtLeast(CampaignRole.GM), Is.True);
    }

    [Test]
    public void IsAtLeast_GM_MeetsPlayer()
    {
        Assert.That(CampaignRole.GM.IsAtLeast(CampaignRole.Player), Is.True);
    }

    [Test]
    public void IsAtLeast_GM_MeetsObserver()
    {
        Assert.That(CampaignRole.GM.IsAtLeast(CampaignRole.Observer), Is.True);
    }

    [Test]
    public void IsAtLeast_Player_DoesNotMeetGM()
    {
        Assert.That(CampaignRole.Player.IsAtLeast(CampaignRole.GM), Is.False);
    }

    [Test]
    public void IsAtLeast_Player_MeetsPlayer()
    {
        Assert.That(CampaignRole.Player.IsAtLeast(CampaignRole.Player), Is.True);
    }

    [Test]
    public void IsAtLeast_Player_MeetsObserver()
    {
        Assert.That(CampaignRole.Player.IsAtLeast(CampaignRole.Observer), Is.True);
    }

    [Test]
    public void IsAtLeast_Observer_DoesNotMeetGM()
    {
        Assert.That(CampaignRole.Observer.IsAtLeast(CampaignRole.GM), Is.False);
    }

    [Test]
    public void IsAtLeast_Observer_DoesNotMeetPlayer()
    {
        Assert.That(CampaignRole.Observer.IsAtLeast(CampaignRole.Player), Is.False);
    }

    [Test]
    public void IsAtLeast_Observer_MeetsObserver()
    {
        Assert.That(CampaignRole.Observer.IsAtLeast(CampaignRole.Observer), Is.True);
    }
}
