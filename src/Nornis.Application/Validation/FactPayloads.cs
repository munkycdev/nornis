namespace Nornis.Application.Validation;

/// <summary>
/// Deserialization target for AddFact ProposedValueJson validation.
/// </summary>
public record AddFactPayload(
    string Predicate,
    string Value,
    decimal? Confidence,
    string? TruthState,
    string? Visibility);

/// <summary>
/// Deserialization target for UpdateFact ProposedValueJson validation.
/// </summary>
public record UpdateFactPayload(
    string? Value,
    decimal? Confidence,
    string? TruthState,
    string? Visibility);
