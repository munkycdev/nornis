namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// A world invite as seen by a managing GM. <c>Code</c> is the redemption token; the Web app
/// builds the shareable <c>nornis.app/invite/{Code}</c> URL from it. <c>Status</c> is the
/// invite's current redeemability (Active/Revoked/Expired/Exhausted).
/// </summary>
public record WorldInviteResponse(
    Guid Id,
    Guid WorldId,
    string Code,
    string Role,
    string Status,
    int UseCount,
    int? MaxUses,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);
