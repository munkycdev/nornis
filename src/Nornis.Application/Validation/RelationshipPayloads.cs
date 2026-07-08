namespace Nornis.Application.Validation;

/// <summary>
/// Deserialization target for AddRelationship ProposedValueJson validation.
/// Each endpoint is referenced by id OR by name — names cover artifacts created in the
/// same review batch, resolved by the applicator at accept time (by which point the
/// batch's CreateArtifact proposals have been applied).
/// </summary>
public record AddRelationshipPayload(
    Guid? ArtifactAId,
    Guid? ArtifactBId,
    string Type,
    string? Description,
    decimal? Confidence,
    string? TruthState,
    string? Visibility,
    string? ArtifactAName = null,
    string? ArtifactBName = null);

/// <summary>
/// Deserialization target for UpdateRelationship ProposedValueJson validation.
/// </summary>
public record UpdateRelationshipPayload(
    string? Type,
    string? Description,
    decimal? Confidence,
    string? TruthState,
    string? Visibility);
