namespace Nornis.Api.Contracts.Responses;

public record CharacterResponse(
    Guid Id,
    Guid WorldId,
    Guid WorldMemberId,
    string Name,
    string? Description,
    Guid? ArtifactId,
    IReadOnlyList<Guid> CampaignIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
