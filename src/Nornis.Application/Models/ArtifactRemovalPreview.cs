namespace Nornis.Application.Models;

/// <summary>
/// What removing an artifact from canon will delete, for a pre-confirmation summary. Only
/// this artifact and the knowledge attached to it is affected — the artifacts on the far end
/// of each relationship survive.
/// </summary>
public record ArtifactRemovalPreview(
    string ArtifactName,
    string ArtifactType,
    int FactCount,
    IReadOnlyList<string> Relationships,
    int MapPinCount,
    int CharacterLinksToClear);
