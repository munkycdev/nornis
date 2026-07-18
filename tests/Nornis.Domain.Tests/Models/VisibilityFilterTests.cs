using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// Truth table for the ownership-aware visibility policy. Every read surface and every
/// test fake delegates to CanSee, so this table is the single specification of who may
/// see what.
/// </summary>
[TestFixture]
[Category("Feature: content-visibility")]
public class VisibilityFilterTests
{
    private static readonly Guid Me = Guid.NewGuid();
    private static readonly Guid SomeoneElse = Guid.NewGuid();

    [Test]
    public void ForRole_Gm_IsUnrestricted()
    {
        var filter = VisibilityFilter.ForRole(WorldRole.GM, Me);

        Assert.That(filter.CanSee(VisibilityScope.PartyVisible, null), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.GMOnly, null), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.Private, Me), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.Private, SomeoneElse), Is.True, "GMs see all Private content");
        Assert.That(filter.CanSee(VisibilityScope.Private, null), Is.True, "GMs see unattributed Private rows");
    }

    [Test]
    public void ForRole_Player_SeesOwnPrivateOnly()
    {
        var filter = VisibilityFilter.ForRole(WorldRole.Player, Me);

        Assert.That(filter.CanSee(VisibilityScope.PartyVisible, SomeoneElse), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.GMOnly, Me), Is.False);
        Assert.That(filter.CanSee(VisibilityScope.Private, Me), Is.True, "own Private is visible");
        Assert.That(filter.CanSee(VisibilityScope.Private, SomeoneElse), Is.False, "another user's Private is not");
        Assert.That(filter.CanSee(VisibilityScope.Private, null), Is.False, "unattributed Private fails closed");
    }

    [Test]
    public void ForRole_Observer_SeesPartyVisibleOnly()
    {
        var filter = VisibilityFilter.ForRole(WorldRole.Observer, Me);

        Assert.That(filter.CanSee(VisibilityScope.PartyVisible, null), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.GMOnly, Me), Is.False);
        Assert.That(filter.CanSee(VisibilityScope.Private, Me), Is.False, "not even their own Private");
    }

    [Test]
    public void All_IsUnrestricted()
    {
        Assert.That(VisibilityFilter.All.CanSee(VisibilityScope.Private, null), Is.True);
        Assert.That(VisibilityFilter.All.CanSee(VisibilityScope.Private, SomeoneElse), Is.True);
        Assert.That(VisibilityFilter.All.CanSee(VisibilityScope.GMOnly, null), Is.True);
    }

    [Test]
    public void ForSourceContext_PrivateSource_LimitedToCreatorsPrivate()
    {
        var filter = VisibilityFilter.ForSourceContext(VisibilityScope.Private, Me);

        Assert.That(filter.CanSee(VisibilityScope.Private, Me), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.Private, SomeoneElse), Is.False,
            "another user's Private must not enter this source's AI context");
        Assert.That(filter.CanSee(VisibilityScope.Private, null), Is.False);
        Assert.That(filter.CanSee(VisibilityScope.PartyVisible, null), Is.False,
            "Private-source context is Private-only (pre-existing scope model)");
    }

    [Test]
    public void ForSourceContext_GmOnlySource_SeesGmAndParty()
    {
        var filter = VisibilityFilter.ForSourceContext(VisibilityScope.GMOnly, Me);

        Assert.That(filter.CanSee(VisibilityScope.GMOnly, null), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.PartyVisible, null), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.Private, Me), Is.False);
    }

    [Test]
    public void ForSourceContext_PartyVisibleSource_SeesPartyOnly()
    {
        var filter = VisibilityFilter.ForSourceContext(VisibilityScope.PartyVisible, Me);

        Assert.That(filter.CanSee(VisibilityScope.PartyVisible, null), Is.True);
        Assert.That(filter.CanSee(VisibilityScope.GMOnly, null), Is.False);
        Assert.That(filter.CanSee(VisibilityScope.Private, Me), Is.False);
    }
}
