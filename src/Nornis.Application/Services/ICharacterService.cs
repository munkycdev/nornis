using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ICharacterService
{
    Task<AppResult<Character>> CreateAsync(CreateCharacterCommand command, CancellationToken ct);

    /// <summary>The character as <paramref name="actingUserId"/> may be told about it — see <see cref="CharacterView"/>.</summary>
    Task<AppResult<CharacterView>> GetByIdAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct);

    /// <summary>
    /// Every character in the world, each as <paramref name="actingUserId"/> may be told about
    /// it — or, with <paramref name="mineOnly"/>, only those of the acting member's own player.
    /// </summary>
    Task<AppResult<IReadOnlyList<CharacterView>>> ListByWorldAsync(
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct,
        bool mineOnly = false);

    /// <summary>
    /// Reduces characters to what <paramref name="actingUserId"/> may be told about them. The
    /// read paths above already apply this; it is public so a mutation's result — the entity
    /// the caller just changed — leaves through the same gate before it is shown to anyone,
    /// including the actor. Claiming a character does not widen what its new owner may see
    /// of the artifact behind it.
    /// </summary>
    Task<IReadOnlyList<CharacterView>> ProjectForReaderAsync(
        IReadOnlyList<Character> characters,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct);

    /// <summary>
    /// The character as a place to go: its owner, its campaigns, and the linked artifact's
    /// record filtered to <paramref name="role"/>. Readable by every world member.
    /// </summary>
    Task<AppResult<CharacterDossier>> GetDossierAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct);

    /// <summary>Replaces the written sheet. Steward or GM.</summary>
    Task<AppResult<Character>> UpdateSheetAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        string? sheet,
        CancellationToken ct);

    /// <summary>Shares the written sheet with the world, or stops. The steward only: the linked member, or the GM while there is none.</summary>
    Task<AppResult<Character>> SetSheetSharingAsync(
        Guid characterId,
        Guid worldId,
        Guid actingUserId,
        WorldRole role,
        bool sharedWithParty,
        CancellationToken ct);

    /// <summary>Attaches an existing source as a dated snapshot of the sheet. Steward or GM.</summary>
    Task<AppResult<CharacterSheetSnapshot>> AttachSnapshotAsync(
        Guid characterId,
        Guid worldId,
        Guid sourceId,
        DateTimeOffset asOf,
        string? note,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct);

    /// <summary>Detaches a snapshot. The source is kept. Steward or GM.</summary>
    Task<AppResult> DetachSnapshotAsync(
        Guid characterId,
        Guid worldId,
        Guid snapshotId,
        Guid actingUserId,
        WorldRole role,
        CancellationToken ct);

    Task<AppResult<Character>> UpdateAsync(UpdateCharacterCommand command, CancellationToken ct);

    /// <summary>
    /// Moves an existing character to the acting member's own player — e.g. a player taking
    /// over a character the GM created or imported before they joined. Any non-Observer
    /// member may claim; tables are small and trusted, and a character can always be claimed
    /// back. To take a whole player, see <see cref="IPlayerService.ClaimAsync"/>.
    /// </summary>
    Task<AppResult<Character>> ClaimAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    Task<AppResult> DeleteAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
