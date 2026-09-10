namespace Nornis.Api.Contracts.Responses;

public record CharacterResponse(
    Guid Id,
    Guid WorldId,
    Guid PlayerId,
    string PlayerName,
    string Name,
    string? Description,
    Guid? ArtifactId,
    IReadOnlyList<Guid> CampaignIds,
    DateTimeOffset? SheetUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
