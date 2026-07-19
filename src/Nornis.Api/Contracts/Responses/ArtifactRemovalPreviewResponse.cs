namespace Nornis.Api.Contracts.Responses;

public record ArtifactRemovalPreviewResponse(
    string ArtifactName,
    string ArtifactType,
    int FactCount,
    IReadOnlyList<string> Relationships,
    int MapPinCount,
    int CharacterLinksToClear);
