namespace Nornis.Api.Contracts.Responses;

/// <summary>What this source's extraction contributed to the record, reader-visible only.</summary>
public record SourceKnowledgeResponse(
    IReadOnlyList<SourceKnowledgeArtifactResponse> Artifacts,
    IReadOnlyList<SourceKnowledgeFactResponse> Facts,
    IReadOnlyList<SourceKnowledgeRelationshipResponse> Relationships);

public record SourceKnowledgeArtifactResponse(
    Guid ArtifactId,
    string Name,
    string Type,
    string? Quote,
    string? Slug = null);

public record SourceKnowledgeFactResponse(
    Guid FactId,
    Guid ArtifactId,
    string ArtifactName,
    string Predicate,
    string Value,
    string TruthState,
    string Visibility,
    string? Quote,
    string? ArtifactSlug = null);

public record SourceKnowledgeRelationshipResponse(
    Guid RelationshipId,
    Guid ArtifactAId,
    string ArtifactAName,
    string Type,
    Guid ArtifactBId,
    string ArtifactBName,
    string? Quote,
    string? ArtifactASlug = null,
    string? ArtifactBSlug = null);
