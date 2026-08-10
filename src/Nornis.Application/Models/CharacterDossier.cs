using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// A character read as a place to go: who plays them, where they have played, and what the
/// knowledge graph knows about the person they are linked to.
/// </summary>
/// <param name="Record">
/// Null when the character is not linked to an artifact <em>and</em> when it is linked to one
/// the reader may not see. The two must stay indistinguishable: a distinguishable "linked but
/// hidden" state tells a player that a GM-only artifact exists bearing their character's name.
/// </param>
/// <param name="Sheet">
/// The player's written sheet, or null when this reader may not read it — which also covers
/// the case where none was ever written. A reader who gets null cannot tell the two apart,
/// and nothing in the product needs them to.
/// </param>
/// <param name="SheetSharedWithParty">
/// Only meaningful to a reader who may edit; false for everyone else. Someone reading a
/// shared sheet knows it is shared because they can read it, and someone who cannot read it
/// has no business knowing whether one exists to share.
/// </param>
public record CharacterDossier(
    Character Character,
    string OwnerDisplayName,
    IReadOnlyList<string> CampaignNames,
    CharacterRecord? Record,
    string? Sheet,
    bool SheetSharedWithParty,
    bool CanEditSheet,
    bool CanShareSheet,
    IReadOnlyList<CharacterSnapshotView> Snapshots);

/// <summary>
/// One attached sheet photograph, as this reader may see it. Snapshots whose source the
/// reader may not read are absent entirely rather than rendered without a title — the title
/// is not the sensitive part, the existence of the source is.
/// </summary>
public record CharacterSnapshotView(
    Guid Id,
    Guid SourceId,
    string SourceTitle,
    DateTimeOffset AsOf,
    string? Note);

/// <summary>
/// The linked artifact as this reader may see it. Every element here arrived already
/// visibility-filtered from <c>ArtifactService.GetDetailAsync</c>; nothing in the dossier
/// path re-queries facts or relationships, so there is one filter and not two.
/// </summary>
/// <param name="TotalFactCount">
/// Facts the reader may see, before the display cap. Counts only visible facts, so it can
/// never become a count of withheld material.
/// </param>
public record CharacterRecord(
    Guid ArtifactId,
    string ArtifactName,
    string? Summary,
    IReadOnlyList<ArtifactFact> Facts,
    int TotalFactCount,
    IReadOnlyList<CharacterRecordGroup> Groups);

/// <param name="TotalCount">
/// Visible artifacts of this type before the per-group cap, so the UI can say "24 of 31"
/// rather than implying the list is complete.
/// </param>
public record CharacterRecordGroup(
    ArtifactType Type,
    IReadOnlyList<Artifact> Artifacts,
    int TotalCount);
