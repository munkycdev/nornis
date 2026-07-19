using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

public interface IWorldInviteService
{
    /// <summary>Mints a new reusable invite link. GM-only.</summary>
    Task<AppResult<WorldInvite>> CreateAsync(CreateInviteCommand command, CancellationToken ct);

    /// <summary>Lists a world's invites, newest first. GM-only.</summary>
    Task<AppResult<IReadOnlyList<WorldInvite>>> ListAsync(Guid worldId, Guid actingUserId, CancellationToken ct);

    /// <summary>Revokes an invite so it can no longer be redeemed. GM-only.</summary>
    Task<AppResult<WorldInvite>> RevokeAsync(Guid worldId, Guid inviteId, Guid actingUserId, CancellationToken ct);

    /// <summary>
    /// Describes an invite for the landing page (world name, granted role, validity).
    /// No world membership required — the caller is a prospective member.
    /// </summary>
    Task<AppResult<InvitePreview>> PreviewAsync(string code, CancellationToken ct);

    /// <summary>
    /// Redeems an invite for <paramref name="userId"/>, adding them to the world with the
    /// invite's role. Idempotent for existing members. No world membership required.
    /// </summary>
    Task<AppResult<InviteRedemption>> RedeemAsync(string code, Guid userId, CancellationToken ct);
}
