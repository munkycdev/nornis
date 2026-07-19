using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class WorldInviteServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();
    private const string WorldName = "Black Harbor";

    private InMemoryWorldInviteRepository _inviteRepository = null!;
    private InMemoryWorldMemberRepository _memberRepository = null!;
    private StubInviteCodeGenerator _codeGenerator = null!;
    private WorldInviteService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _inviteRepository = new InMemoryWorldInviteRepository();
        _memberRepository = new InMemoryWorldMemberRepository();
        _codeGenerator = new StubInviteCodeGenerator();
        _sut = new WorldInviteService(_inviteRepository, _memberRepository, _codeGenerator);
    }

    private void SeedGm() => _memberRepository.CreateAsync(new WorldMember
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        UserId = GmUserId,
        Role = WorldRole.GM,
        JoinedAt = DateTimeOffset.UtcNow
    });

    private void SeedMember(Guid userId, WorldRole role) => _memberRepository.CreateAsync(new WorldMember
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        UserId = userId,
        Role = role,
        JoinedAt = DateTimeOffset.UtcNow
    });

    private WorldInvite SeedInvite(
        string code = "code-1",
        WorldRole role = WorldRole.Player,
        DateTimeOffset? expiresAt = null,
        int? maxUses = null,
        int useCount = 0,
        DateTimeOffset? revokedAt = null,
        Guid? worldId = null)
    {
        var wId = worldId ?? WorldId;
        var invite = new WorldInvite
        {
            Id = Guid.NewGuid(),
            WorldId = wId,
            Code = code,
            Role = role,
            CreatedByUserId = GmUserId,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ExpiresAt = expiresAt,
            MaxUses = maxUses,
            UseCount = useCount,
            RevokedAt = revokedAt,
            World = new World { Id = wId, Name = WorldName }
        };
        _inviteRepository.Seed(invite);
        return invite;
    }

    // ------------------------------------------------------------------- Create --

    [Test]
    public async Task CreateAsync_AsGm_CreatesInvite()
    {
        SeedGm();
        var command = new CreateInviteCommand(WorldId, GmUserId, WorldRole.Player, MaxUses: 5);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Code, Is.EqualTo("code-1"));
        Assert.That(result.Value.Role, Is.EqualTo(WorldRole.Player));
        Assert.That(result.Value.MaxUses, Is.EqualTo(5));
        Assert.That(result.Value.UseCount, Is.EqualTo(0));
        Assert.That(_inviteRepository.Invites, Has.Count.EqualTo(1));
    }

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task CreateAsync_AsNonGm_Returns403(WorldRole actingRole)
    {
        SeedMember(GmUserId, actingRole);
        var command = new CreateInviteCommand(WorldId, GmUserId, WorldRole.Player);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_inviteRepository.Invites, Is.Empty);
    }

    [Test]
    public async Task CreateAsync_NotAMember_Returns403()
    {
        var command = new CreateInviteCommand(WorldId, Guid.NewGuid(), WorldRole.Player);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task CreateAsync_UndefinedRole_Returns400()
    {
        SeedGm();
        var command = new CreateInviteCommand(WorldId, GmUserId, (WorldRole)99);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task CreateAsync_PastExpiry_Returns400()
    {
        SeedGm();
        var command = new CreateInviteCommand(WorldId, GmUserId, WorldRole.Player,
            ExpiresAt: DateTimeOffset.UtcNow.AddDays(-1));

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [TestCase(0)]
    [TestCase(-3)]
    public async Task CreateAsync_NonPositiveMaxUses_Returns400(int maxUses)
    {
        SeedGm();
        var command = new CreateInviteCommand(WorldId, GmUserId, WorldRole.Player, MaxUses: maxUses);

        var result = await _sut.CreateAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    // --------------------------------------------------------------------- List --

    [Test]
    public async Task ListAsync_AsGm_ReturnsInvites()
    {
        SeedGm();
        SeedInvite("code-a");
        SeedInvite("code-b");

        var result = await _sut.ListAsync(WorldId, GmUserId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ListAsync_AsNonGm_Returns403()
    {
        var playerId = Guid.NewGuid();
        SeedMember(playerId, WorldRole.Player);

        var result = await _sut.ListAsync(WorldId, playerId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    // ------------------------------------------------------------------- Revoke --

    [Test]
    public async Task RevokeAsync_AsGm_SetsRevokedAt()
    {
        SeedGm();
        var invite = SeedInvite();

        var result = await _sut.RevokeAsync(WorldId, invite.Id, GmUserId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.RevokedAt, Is.Not.Null);
    }

    [Test]
    public async Task RevokeAsync_AsNonGm_Returns403()
    {
        var playerId = Guid.NewGuid();
        SeedMember(playerId, WorldRole.Player);
        var invite = SeedInvite();

        var result = await _sut.RevokeAsync(WorldId, invite.Id, playerId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task RevokeAsync_UnknownInvite_Returns404()
    {
        SeedGm();

        var result = await _sut.RevokeAsync(WorldId, Guid.NewGuid(), GmUserId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RevokeAsync_InviteFromAnotherWorld_Returns404()
    {
        SeedGm();
        var invite = SeedInvite(worldId: Guid.NewGuid());

        var result = await _sut.RevokeAsync(WorldId, invite.Id, GmUserId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ------------------------------------------------------------------ Preview --

    [Test]
    public async Task PreviewAsync_ActiveInvite_ReturnsWorldAndRole()
    {
        SeedInvite(role: WorldRole.Observer);

        var result = await _sut.PreviewAsync("code-1", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WorldName, Is.EqualTo(WorldName));
        Assert.That(result.Value.Role, Is.EqualTo(WorldRole.Observer));
        Assert.That(result.Value.Status, Is.EqualTo(InviteStatus.Active));
    }

    [Test]
    public async Task PreviewAsync_UnknownCode_Returns404()
    {
        var result = await _sut.PreviewAsync("nope", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task PreviewAsync_RevokedInvite_ReportsRevokedStatus()
    {
        SeedInvite(revokedAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _sut.PreviewAsync("code-1", CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Status, Is.EqualTo(InviteStatus.Revoked));
    }

    // ------------------------------------------------------------------- Redeem --

    [Test]
    public async Task RedeemAsync_ActiveInvite_AddsMemberWithRoleAndIncrementsUse()
    {
        var invite = SeedInvite(role: WorldRole.Player, maxUses: 3);
        var newUserId = Guid.NewGuid();

        var result = await _sut.RedeemAsync("code-1", newUserId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AlreadyMember, Is.False);
        Assert.That(result.Value.WorldId, Is.EqualTo(WorldId));

        var member = _memberRepository.Members.SingleOrDefault(m => m.UserId == newUserId);
        Assert.That(member, Is.Not.Null);
        Assert.That(member!.Role, Is.EqualTo(WorldRole.Player));
        Assert.That(invite.UseCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RedeemAsync_AlreadyMember_IsIdempotentAndDoesNotConsumeUse()
    {
        var invite = SeedInvite(maxUses: 3);
        var userId = Guid.NewGuid();
        SeedMember(userId, WorldRole.Player);

        var result = await _sut.RedeemAsync("code-1", userId, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.AlreadyMember, Is.True);
        Assert.That(invite.UseCount, Is.EqualTo(0));
        // No duplicate membership created.
        Assert.That(_memberRepository.Members.Count(m => m.UserId == userId), Is.EqualTo(1));
    }

    [Test]
    public async Task RedeemAsync_UnknownCode_Returns404()
    {
        var result = await _sut.RedeemAsync("nope", Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RedeemAsync_RevokedInvite_Returns409()
    {
        SeedInvite(revokedAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _sut.RedeemAsync("code-1", Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(_memberRepository.Members, Is.Empty);
    }

    [Test]
    public async Task RedeemAsync_ExpiredInvite_Returns409()
    {
        SeedInvite(expiresAt: DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _sut.RedeemAsync("code-1", Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("invite_expired"));
    }

    [Test]
    public async Task RedeemAsync_ExhaustedInvite_Returns409()
    {
        SeedInvite(maxUses: 2, useCount: 2);

        var result = await _sut.RedeemAsync("code-1", Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("invite_exhausted"));
    }

    [Test]
    public async Task RedeemAsync_ConcurrencyConflict_Returns409AndAddsNoMember()
    {
        SeedInvite(maxUses: 1);
        _inviteRepository.ThrowConcurrencyOnNextUpdate = true;

        var result = await _sut.RedeemAsync("code-1", Guid.NewGuid(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(_memberRepository.Members, Is.Empty);
    }
}
