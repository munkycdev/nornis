namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// A GM's reveal: promote the listed GM-only artifacts, facts, and relationships to the
/// party, applying any belief-corrections (fact truth-state changes) in the same event.
/// Omitted lists are treated as empty.
/// </summary>
public record RevealRequest(
    IReadOnlyList<Guid>? ArtifactIds,
    IReadOnlyList<Guid>? FactIds,
    IReadOnlyList<Guid>? RelationshipIds,
    IReadOnlyList<RevealCorrectionRequest>? Corrections,
    string? Note);

/// <summary>An existing fact to re-truth-state (e.g. to <c>Disputed</c>/<c>False</c>) as the
/// reveal supersedes it.</summary>
public record RevealCorrectionRequest(Guid FactId, string TruthState);
