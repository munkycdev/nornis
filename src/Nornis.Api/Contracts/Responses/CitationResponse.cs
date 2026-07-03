namespace Nornis.Api.Contracts.Responses;

public record CitationResponse(
    string ReferenceId,
    string Type,
    string DisplayName,
    Guid? ArtifactId,
    Guid? FactId,
    Guid? RelationshipId,
    Guid? SourceId);
