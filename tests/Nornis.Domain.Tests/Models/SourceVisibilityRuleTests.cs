using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>
/// Pins the source-visibility rule per role. These expectations are written from the original
/// in-memory predicate in <c>SourceService.CanSeeSource</c>, not from the expression that now
/// implements it — the point is to catch a translation that drifts, and a test derived from the
/// new code could not do that.
///
/// The failure direction that matters: a Private or GM-only source appearing to someone who
/// should not see it. Every "must not see" case below is a leak if it flips.
/// </summary>
[TestFixture]
public class SourceVisibilityRuleTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static Source Make(
        VisibilityScope visibility,
        Guid createdBy,
        SourceProcessingStatus status = SourceProcessingStatus.Processed) => new()
        {
            Id = Guid.NewGuid(),
            WorldId = Guid.NewGuid(),
            Type = SourceType.SessionNote,
            Title = "A note",
            Visibility = visibility,
            ProcessingStatus = status,
            CreatedByUserId = createdBy,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static bool CanSee(Source source, Guid userId, WorldRole role) =>
        SourceVisibilityRule.Compile(userId, role)(source);

    // ------------------------------------------------------------------ GM

    [Test]
    public void Gm_SeesEveryScope_IncludingOtherPeoplesPrivate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CanSee(Make(VisibilityScope.PartyVisible, Other), Owner, WorldRole.GM), Is.True);
            Assert.That(CanSee(Make(VisibilityScope.Private, Other), Owner, WorldRole.GM), Is.True);
            Assert.That(CanSee(Make(VisibilityScope.GMOnly, Other), Owner, WorldRole.GM), Is.True);
        });
    }

    [Test]
    public void Gm_SeesOtherPeoplesDrafts()
    {
        var draft = Make(VisibilityScope.PartyVisible, Other, SourceProcessingStatus.Draft);

        Assert.That(CanSee(draft, Owner, WorldRole.GM), Is.True);
    }

    // ------------------------------------------------------------------ Player, owner

    [Test]
    public void PlayerOwner_SeesTheirOwnPrivateAndDraft()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CanSee(Make(VisibilityScope.Private, Owner), Owner, WorldRole.Player), Is.True);
            Assert.That(
                CanSee(Make(VisibilityScope.PartyVisible, Owner, SourceProcessingStatus.Draft), Owner, WorldRole.Player),
                Is.True);
        });
    }

    [Test]

    [Category("Authorization")]
    public void PlayerOwner_StillCannotSeeGmOnly()
    {
        // Owning a source does not grant the GM scope — a GM-only note authored by this player
        // (possible via a role change) stays hidden.
        Assert.That(CanSee(Make(VisibilityScope.GMOnly, Owner), Owner, WorldRole.Player), Is.False);
    }

    // ------------------------------------------------------------------ Player, not the owner

    [Test]

    [Category("Authorization")]
    public void PlayerOther_SeesPartyVisibleOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CanSee(Make(VisibilityScope.PartyVisible, Owner), Other, WorldRole.Player), Is.True);
            Assert.That(CanSee(Make(VisibilityScope.Private, Owner), Other, WorldRole.Player), Is.False,
                "another player's Private note must never appear");
            Assert.That(CanSee(Make(VisibilityScope.GMOnly, Owner), Other, WorldRole.Player), Is.False);
        });
    }

    [Test]

    [Category("Authorization")]
    public void PlayerOther_CannotSeeSomeoneElsesDraft_EvenWhenPartyVisible()
    {
        var draft = Make(VisibilityScope.PartyVisible, Owner, SourceProcessingStatus.Draft);

        Assert.That(CanSee(draft, Other, WorldRole.Player), Is.False,
            "an unfinished note belongs to its author until it is finished");
    }

    // ------------------------------------------------------------------ Observer

    [Test]

    [Category("Authorization")]
    public void Observer_SeesPartyVisibleOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CanSee(Make(VisibilityScope.PartyVisible, Owner), Other, WorldRole.Observer), Is.True);
            Assert.That(CanSee(Make(VisibilityScope.Private, Owner), Other, WorldRole.Observer), Is.False);
            Assert.That(CanSee(Make(VisibilityScope.GMOnly, Owner), Other, WorldRole.Observer), Is.False);
        });
    }

    [Test]
    public void AnonymousObserver_MatchesNothingByOwnership()
    {
        // The public world page reads as Observer with an empty id. An unattributable row
        // (CreatedByUserId == Guid.Empty) must not match the ownership test and become visible.
        var orphanPrivate = Make(VisibilityScope.Private, Guid.Empty);
        var orphanDraft = Make(VisibilityScope.PartyVisible, Guid.Empty, SourceProcessingStatus.Draft);

        Assert.Multiple(() =>
        {
            Assert.That(CanSee(orphanPrivate, Guid.Empty, WorldRole.Observer), Is.False);
            Assert.That(CanSee(orphanDraft, Guid.Empty, WorldRole.Observer), Is.False);
        });
    }

    [Test]
    public void AnonymousPlayer_DoesNotMatchOrphanRowsByOwnership()
    {
        // Belt and braces: even if a Player-role caller ever arrived without an identity, an
        // empty id must not act as a wildcard owner.
        Assert.Multiple(() =>
        {
            Assert.That(CanSee(Make(VisibilityScope.Private, Guid.Empty), Guid.Empty, WorldRole.Player), Is.False);
            Assert.That(
                CanSee(Make(VisibilityScope.PartyVisible, Guid.Empty, SourceProcessingStatus.Draft), Guid.Empty, WorldRole.Player),
                Is.False);
        });
    }
}
