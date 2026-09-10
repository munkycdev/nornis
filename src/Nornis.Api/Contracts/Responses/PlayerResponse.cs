namespace Nornis.Api.Contracts.Responses;

/// <param name="WorldMemberId">Null means not on Nornis. Nothing else.</param>
/// <param name="Role">The linked member's role; null for a player who is not on Nornis.</param>
public record PlayerResponse(
    Guid Id,
    Guid WorldId,
    string Name,
    Guid? WorldMemberId,
    string? Role);
