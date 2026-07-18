namespace Nornis.Application.Models;

/// <summary>
/// What a reprocess would delete, so the UI can show an honest confirmation.
/// Artifacts this source created but other sources have since built on are kept
/// (listed in <see cref="ArtifactNamesToKeep"/>) — only their contribution from
/// this source is removed.
/// </summary>
public record ReprocessPreview(
    IReadOnlyList<string> ArtifactNamesToDelete,
    IReadOnlyList<string> ArtifactNamesToKeep,
    int FactsToDelete,
    int RelationshipsToDelete,
    int PendingProposalsToDiscard);
