using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Entities;

[TestFixture]
public class WorldInviteTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static WorldInvite Invite(
        DateTimeOffset? expiresAt = null,
        int? maxUses = null,
        int useCount = 0,
        DateTimeOffset? revokedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        WorldId = Guid.NewGuid(),
        Code = "code",
        Role = WorldRole.Player,
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = Now.AddDays(-1),
        ExpiresAt = expiresAt,
        MaxUses = maxUses,
        UseCount = useCount,
        RevokedAt = revokedAt
    };

    [Test]
    public void StatusAt_NoLimits_IsActive()
    {
        var invite = Invite();

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Active));
        Assert.That(invite.CanBeRedeemed(Now), Is.True);
    }

    [Test]
    public void StatusAt_Revoked_IsRevoked()
    {
        var invite = Invite(revokedAt: Now.AddHours(-1));

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Revoked));
        Assert.That(invite.CanBeRedeemed(Now), Is.False);
    }

    [Test]
    public void StatusAt_ExpiryInPast_IsExpired()
    {
        var invite = Invite(expiresAt: Now.AddSeconds(-1));

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Expired));
    }

    [Test]
    public void StatusAt_ExpiryExactlyNow_IsExpired()
    {
        // Boundary: an invite valid "until" a moment is expired at that moment.
        var invite = Invite(expiresAt: Now);

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Expired));
    }

    [Test]
    public void StatusAt_ExpiryInFuture_IsActive()
    {
        var invite = Invite(expiresAt: Now.AddDays(1));

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Active));
    }

    [Test]
    public void StatusAt_UsesRemaining_IsActive()
    {
        var invite = Invite(maxUses: 5, useCount: 4);

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Active));
    }

    [Test]
    public void StatusAt_UsesReached_IsExhausted()
    {
        var invite = Invite(maxUses: 5, useCount: 5);

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Exhausted));
    }

    [Test]
    public void StatusAt_RevokedTakesPrecedenceOverExpiryAndExhaustion()
    {
        var invite = Invite(
            expiresAt: Now.AddSeconds(-1),
            maxUses: 1,
            useCount: 1,
            revokedAt: Now.AddHours(-1));

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Revoked));
    }

    [Test]
    public void StatusAt_ExpiryTakesPrecedenceOverExhaustion()
    {
        var invite = Invite(expiresAt: Now.AddSeconds(-1), maxUses: 1, useCount: 1);

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Expired));
    }

    [Test]
    public void StatusAt_UnlimitedUses_NeverExhausted()
    {
        var invite = Invite(maxUses: null, useCount: 9999);

        Assert.That(invite.StatusAt(Now), Is.EqualTo(InviteStatus.Active));
    }
}
