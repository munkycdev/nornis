using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IShelfLinkService
{
    /// <summary>
    /// Mints a shelf link for a player who is not on Nornis, revoking their standing one if
    /// they have it. GM only. A linked player is refused: they have the Library page.
    /// </summary>
    Task<AppResult<ShelfLink>> CreateAsync(Guid worldId, Guid playerId, Guid actingUserId, WorldRole actingRole, CancellationToken ct);

    /// <summary>Every standing link in the world. GM only — the codes are secrets.</summary>
    Task<AppResult<IReadOnlyList<ShelfLink>>> ListActiveAsync(Guid worldId, WorldRole actingRole, CancellationToken ct);

    /// <summary>Revokes a link so it never opens again. GM only; idempotent.</summary>
    Task<AppResult> RevokeAsync(Guid worldId, Guid linkId, WorldRole actingRole, CancellationToken ct);

    /// <summary>
    /// Opens a link: the world, the player, and the party shelf. No membership — the caller is
    /// whoever holds the code. Unknown and revoked codes fail with one and the same error.
    /// </summary>
    Task<AppResult<Shelf>> OpenAsync(string code, CancellationToken ct);

    /// <summary>A read URL for one document on the shelf a link opens, under the same rule.</summary>
    Task<AppResult<LibraryDownload>> DownloadAsync(string code, Guid documentId, CancellationToken ct);
}
