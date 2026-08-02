using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Exceptions;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class WorldInviteService : IWorldInviteService
{
    private readonly IWorldInviteRepository _inviteRepository;
    private readonly IWorldMemberRepository _memberRepository;
    private readonly IInviteCodeGenerator _codeGenerator;

    public WorldInviteService(
        IWorldInviteRepository inviteRepository,
        IWorldMemberRepository memberRepository,
        IInviteCodeGenerator codeGenerator)
    {
        _inviteRepository = inviteRepository;
        _memberRepository = memberRepository;
        _codeGenerator = codeGenerator;
    }

    public async Task<AppResult<WorldInvite>> CreateAsync(CreateInviteCommand command, CancellationToken ct)
    {
        if (!Enum.IsDefined(typeof(WorldRole), command.InvitedRole))
        {
            return AppResult<WorldInvite>.Fail(
                new AppError(400, "invalid_role", "Role must be GM, Player, or Observer."));
        }

        var now = DateTimeOffset.UtcNow;

        if (command.ExpiresAt is { } expiry && expiry <= now)
        {
            return AppResult<WorldInvite>.Fail(
                new AppError(400, "validation_error", "Expiry must be in the future."));
        }

        if (command.MaxUses is { } maxUses && maxUses < 1)
        {
            return AppResult<WorldInvite>.Fail(
                new AppError(400, "validation_error", "Maximum uses must be at least 1."));
        }

        var gmCheck = RequireGm(command.ActingUserRole);
        if (gmCheck is not null)
        {
            return AppResult<WorldInvite>.Fail(gmCheck);
        }

        // A 128-bit random code effectively never collides; the unique index on Code is the
        // last-resort integrity guard.
        var invite = new WorldInvite
        {
            Id = Guid.NewGuid(),
            WorldId = command.WorldId,
            Code = _codeGenerator.Generate(),
            Role = command.InvitedRole,
            CreatedByUserId = command.ActingUserId,
            CreatedAt = now,
            ExpiresAt = command.ExpiresAt,
            MaxUses = command.MaxUses,
            UseCount = 0
        };

        var created = await _inviteRepository.CreateAsync(invite, ct);
        return AppResult<WorldInvite>.Success(created);
    }

    public async Task<AppResult<IReadOnlyList<WorldInvite>>> ListAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gmCheck = RequireGm(actingUserRole);
        if (gmCheck is not null)
        {
            return AppResult<IReadOnlyList<WorldInvite>>.Fail(gmCheck);
        }

        var invites = await _inviteRepository.ListByWorldAsync(worldId, ct);
        return AppResult<IReadOnlyList<WorldInvite>>.Success(invites);
    }

    public async Task<AppResult<WorldInvite>> RevokeAsync(
        Guid worldId, Guid inviteId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct)
    {
        var gmCheck = RequireGm(actingUserRole);
        if (gmCheck is not null)
        {
            return AppResult<WorldInvite>.Fail(gmCheck);
        }

        var invite = await _inviteRepository.GetByIdAsync(inviteId, ct);
        if (invite is null || invite.WorldId != worldId)
        {
            return AppResult<WorldInvite>.Fail(
                new AppError(404, "not_found", "Invite not found in this world."));
        }

        if (invite.RevokedAt is null)
        {
            invite.RevokedAt = DateTimeOffset.UtcNow;
            invite = await _inviteRepository.UpdateAsync(invite, ct);
        }

        return AppResult<WorldInvite>.Success(invite);
    }

    public async Task<AppResult<InvitePreview>> PreviewAsync(string code, CancellationToken ct)
    {
        var invite = await _inviteRepository.GetByCodeAsync(code, ct);
        if (invite is null)
        {
            return AppResult<InvitePreview>.Fail(
                new AppError(404, "not_found", "This invite link is not valid."));
        }

        var preview = new InvitePreview(
            invite.WorldId,
            invite.World.Name,
            invite.Role,
            invite.StatusAt(DateTimeOffset.UtcNow));

        return AppResult<InvitePreview>.Success(preview);
    }

    public async Task<AppResult<InviteRedemption>> RedeemAsync(string code, Guid userId, CancellationToken ct)
    {
        var invite = await _inviteRepository.GetByCodeAsync(code, ct);
        if (invite is null)
        {
            return AppResult<InviteRedemption>.Fail(
                new AppError(404, "not_found", "This invite link is not valid."));
        }

        var worldName = invite.World.Name;

        // Redemption is idempotent: an existing member just lands back in the world and
        // does not consume a use.
        var existing = await _memberRepository.GetByWorldAndUserAsync(invite.WorldId, userId, ct);
        if (existing is not null)
        {
            return AppResult<InviteRedemption>.Success(
                new InviteRedemption(invite.WorldId, worldName, AlreadyMember: true));
        }

        var status = invite.StatusAt(DateTimeOffset.UtcNow);
        if (status != InviteStatus.Active)
        {
            return AppResult<InviteRedemption>.Fail(InvalidInviteError(status));
        }

        // Reserve a use-slot BEFORE adding the member. The RowVersion concurrency token means
        // only one of two racing redemptions can commit the increment for the last slot; the
        // loser gets a conflict and never over-admits past MaxUses.
        invite.UseCount++;
        try
        {
            await _inviteRepository.UpdateAsync(invite, ct);
        }
        catch (ConcurrencyConflictException)
        {
            return AppResult<InviteRedemption>.Fail(
                new AppError(409, "conflict", "This invite was just used by someone else. Please try again."));
        }

        var member = new WorldMember
        {
            Id = Guid.NewGuid(),
            WorldId = invite.WorldId,
            UserId = userId,
            Role = invite.Role,
            JoinedAt = DateTimeOffset.UtcNow
        };
        await _memberRepository.CreateAsync(member, ct);

        return AppResult<InviteRedemption>.Success(
            new InviteRedemption(invite.WorldId, worldName, AlreadyMember: false));
    }

    /// <summary>
    /// Returns a 403 when the acting role is not GM, else null. Takes the role rather than
    /// re-reading the membership row: WorldMemberActionFilter resolved it before the request
    /// reached a controller, and asking again is a second query for an answer already held.
    /// </summary>
    private static AppError? RequireGm(WorldRole actingUserRole) =>
        actingUserRole != WorldRole.GM
            ? new AppError(403, "insufficient_role", "Only a GM can manage world invites.")
            : null;

    private static AppError InvalidInviteError(InviteStatus status) => status switch
    {
        InviteStatus.Revoked => new AppError(409, "invite_revoked", "This invite has been revoked."),
        InviteStatus.Expired => new AppError(409, "invite_expired", "This invite has expired."),
        InviteStatus.Exhausted => new AppError(409, "invite_exhausted", "This invite has reached its maximum number of uses."),
        _ => new AppError(409, "conflict", "This invite can no longer be used.")
    };
}
