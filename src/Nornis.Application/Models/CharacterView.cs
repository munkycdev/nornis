namespace Nornis.Application.Models;

/// <summary>
/// A character as one reader may be told about it. Everything a member may read about a
/// character leaves the Application layer in this shape, never as the entity, because two
/// of the entity's fields say more than their values:
/// </summary>
/// <param name="ArtifactId">
/// The linked artifact's id, or null when there is no link <em>or</em> when the reader may
/// not see the artifact. A character linked to a GM-only artifact must serialize exactly
/// as an unlinked one does; the id alone, with no record beside it, still announces that a
/// GM-only artifact exists bearing this character's name. The pages were already
/// indistinguishable when this was added — the JSON was not, and a test that compared only
/// the record never noticed.
/// </param>
/// <param name="SheetUpdatedAt">
/// When the sheet was last written, or null when the reader may not read the sheet — which
/// also covers the case where none was ever written. A timestamp on a sheet the reader
/// cannot open tells them a sheet exists to be shared, which is the disclosure
/// <c>CharacterDossier.SheetSharedWithParty</c> already declines to make.
/// </param>
public record CharacterView(
    Guid Id,
    Guid WorldId,
    Guid WorldMemberId,
    string Name,
    string? Description,
    Guid? ArtifactId,
    IReadOnlyList<Guid> CampaignIds,
    DateTimeOffset? SheetUpdatedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
