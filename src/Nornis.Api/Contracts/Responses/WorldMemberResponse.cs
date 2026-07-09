namespace Nornis.Api.Contracts.Responses;

public record WorldMemberResponse(
    Guid Id,
    Guid WorldId,
    Guid UserId,
    string Role,
    string? DisplayName,
    DateTimeOffset JoinedAt);
