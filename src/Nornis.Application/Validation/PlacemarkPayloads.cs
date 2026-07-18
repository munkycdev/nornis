namespace Nornis.Application.Validation;

/// <summary>
/// Deserialization target for AddPlacemark ProposedValueJson: pin an existing artifact
/// (by id, or by name for reviewer-resolved ambiguity) onto a map attachment.
/// </summary>
public record AddPlacemarkPayload(
    Guid? ArtifactId,
    string? ArtifactName,
    Guid AttachmentId,
    decimal X,
    decimal Y,
    string? Label,
    decimal? Confidence);
