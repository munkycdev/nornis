namespace Nornis.Api.Contracts.Responses;

/// <param name="Record">
/// Absent both when the character is unlinked and when the reader may not see the artifact
/// it links to. The two cases must serialize identically — see
/// <c>CharacterService.ResolveRecordAsync</c> for why.
/// </param>
public record CharacterDossierResponse(
    CharacterResponse Character,
    string OwnerDisplayName,
    IReadOnlyList<string> CampaignNames,
    CharacterRecordResponse? Record,
    string? Sheet,
    bool SheetSharedWithParty,
    bool CanEditSheet,
    bool CanShareSheet,
    IReadOnlyList<CharacterSnapshotResponse> Snapshots,
    IReadOnlyList<UnreconciledItemResponse> UnreconciledItems);

/// <summary>An item the visible record connects to the character that the readable sheet never names.</summary>
public record UnreconciledItemResponse(
    Guid ArtifactId,
    string Name,
    string Type);

public record CharacterSnapshotResponse(
    Guid Id,
    Guid SourceId,
    string SourceTitle,
    DateTimeOffset AsOf,
    string? Note);

public record CharacterRecordResponse(
    Guid ArtifactId,
    string ArtifactName,
    string? Summary,
    IReadOnlyList<ArtifactFactResponse> Facts,
    int TotalFactCount,
    IReadOnlyList<CharacterRecordGroupResponse> Groups);

public record CharacterRecordGroupResponse(
    string Type,
    IReadOnlyList<ConnectedArtifactResponse> Artifacts,
    int TotalCount);
