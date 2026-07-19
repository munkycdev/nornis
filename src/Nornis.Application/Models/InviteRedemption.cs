namespace Nornis.Application.Models;

/// <summary>
/// The outcome of redeeming an invite. <paramref name="AlreadyMember"/> is true when the
/// caller was already in the world — redemption is idempotent and does not double-count.
/// </summary>
public record InviteRedemption(
    Guid WorldId,
    string WorldName,
    bool AlreadyMember);
