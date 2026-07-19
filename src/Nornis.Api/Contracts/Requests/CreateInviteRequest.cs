namespace Nornis.Api.Contracts.Requests;

public record CreateInviteRequest(
    string Role,
    DateTimeOffset? ExpiresAt = null,
    int? MaxUses = null);
