namespace Nornis.Application.Validation;

/// <summary>
/// Deserialization target for CreateArtifact ProposedValueJson validation.
/// </summary>
public record CreateArtifactPayload(
    string Name,
    string Type,
    string? Summary,
    string? Visibility,
    decimal? Confidence);

/// <summary>
/// Deserialization target for UpdateArtifact ProposedValueJson validation.
/// </summary>
public record UpdateArtifactPayload(
    string? Name,
    string? Summary,
    string? Visibility,
    decimal? Confidence,
    string? Status);

/// <summary>
/// Deserialization target for MergeArtifact ProposedValueJson validation.
/// </summary>
public record MergeArtifactPayload(
    Guid SourceArtifactId,
    string? Name,
    string? Summary,
    string? Visibility,
    decimal? Confidence);
