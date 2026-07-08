namespace Nornis.Application.Validation;

/// <summary>
/// Deserialization target for AddFact ProposedValueJson validation.
/// ArtifactName is a fallback reference for artifacts created in the same review batch:
/// when the proposal's TargetId is null, the applicator resolves the artifact by name at
/// accept time (by which point the batch's CreateArtifact proposal has been applied).
/// </summary>
public record AddFactPayload(
    string Predicate,
    string Value,
    decimal? Confidence,
    string? TruthState,
    string? Visibility,
    string? ArtifactName = null);

/// <summary>
/// Deserialization target for UpdateFact ProposedValueJson validation.
/// </summary>
public record UpdateFactPayload(
    string? Value,
    decimal? Confidence,
    string? TruthState,
    string? Visibility);
