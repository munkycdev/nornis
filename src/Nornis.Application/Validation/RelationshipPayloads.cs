namespace Nornis.Application.Validation;

/// <summary>
/// Deserialization target for AddRelationship ProposedValueJson validation.
/// </summary>
public record AddRelationshipPayload(
    Guid ArtifactAId,
    Guid ArtifactBId,
    string Type,
    string? Description,
    decimal? Confidence,
    string? TruthState,
    string? Visibility);

/// <summary>
/// Deserialization target for UpdateRelationship ProposedValueJson validation.
/// </summary>
public record UpdateRelationshipPayload(
    string? Type,
    string? Description,
    decimal? Confidence,
    string? TruthState,
    string? Visibility);
