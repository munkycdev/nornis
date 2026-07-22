namespace Nornis.Application.Models;

/// <summary>
/// Everything a source's extraction contributed to the record, grouped by kind and
/// limited to what the requesting reader may see. Quotes come from the provenance
/// rows (SourceReferences) the extraction wrote.
/// </summary>
public record SourceKnowledge(
    IReadOnlyList<SourceKnowledgeArtifact> Artifacts,
    IReadOnlyList<SourceKnowledgeFact> Facts,
    IReadOnlyList<SourceKnowledgeRelationship> Relationships);

public record SourceKnowledgeArtifact(
    Guid ArtifactId,
    string Name,
    string Type,
    string? Quote);

public record SourceKnowledgeFact(
    Guid FactId,
    Guid ArtifactId,
    string ArtifactName,
    string Predicate,
    string Value,
    string TruthState,
    string Visibility,
    string? Quote);

public record SourceKnowledgeRelationship(
    Guid RelationshipId,
    Guid ArtifactAId,
    string ArtifactAName,
    string Type,
    Guid ArtifactBId,
    string ArtifactBName,
    string? Quote);

/// <summary>GM-only removal of a single incorrect fact, recorded by a GM note.</summary>
public record RemoveFactCommand(
    Guid WorldId,
    Guid FactId,
    string Note,
    Guid ActingUserId,
    Nornis.Domain.Enums.WorldRole ActingUserRole);
