using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// What an authenticated invitee sees before deciding to join: which world the invite is
/// for, the role it grants, and whether it can still be redeemed.
/// </summary>
public record InvitePreview(
    Guid WorldId,
    string WorldName,
    WorldRole Role,
    InviteStatus Status);
