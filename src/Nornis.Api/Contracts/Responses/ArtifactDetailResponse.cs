namespace Nornis.Api.Contracts.Responses;

public record ArtifactDetailResponse(
    Guid Id,
    Guid WorldId,
    string Type,
    string Name,
    string? Summary,
    string Status,
    string Visibility,
    decimal? Confidence,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ArtifactFactResponse> Facts,
    IReadOnlyList<ArtifactRelationshipResponse> Relationships,
    IReadOnlyList<ConnectedArtifactResponse> ConnectedArtifacts,
    IReadOnlyList<SourceReferenceResponse> SourceReferences);
