namespace Nornis.Api.Contracts.Responses;

public record CampaignMemberResponse(
    Guid Id,
    Guid CampaignId,
    Guid UserId,
    string Role,
    string? DisplayName,
    string? CharacterName,
    DateTimeOffset JoinedAt);
