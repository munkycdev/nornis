namespace Nornis.Api.Contracts.Responses;

/// <summary>What a reprocess would delete, for the confirmation dialog.</summary>
public record ReprocessPreviewResponse(
    IReadOnlyList<string> ArtifactNamesToDelete,
    IReadOnlyList<string> ArtifactNamesToKeep,
    int FactsToDelete,
    int RelationshipsToDelete,
    int PendingProposalsToDiscard,
    int MapPinsToDelete);
