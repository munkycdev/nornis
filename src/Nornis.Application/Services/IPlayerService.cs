using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IPlayerService
{
    /// <summary>Every player in the world, linked and not, named by <see cref="PlayerDisplayName"/>.</summary>
    Task<AppResult<IReadOnlyList<PlayerView>>> ListByWorldAsync(Guid worldId, CancellationToken ct);

    /// <summary>The acting member's own player, which every member has.</summary>
    Task<AppResult<PlayerView>> GetMineAsync(Guid worldId, Guid actingUserId, CancellationToken ct);

    /// <summary>Adds an unlinked player by name. GM only.</summary>
    Task<AppResult<Player>> CreateAsync(Guid worldId, string name, WorldRole actingRole, CancellationToken ct);

    /// <summary>Renames an unlinked player. GM only; a linked player is named by their membership.</summary>
    Task<AppResult<Player>> RenameAsync(Guid playerId, Guid worldId, string name, WorldRole actingRole, CancellationToken ct);

    /// <summary>
    /// Removes an unlinked player with no characters. GM only. A player who still has
    /// characters is refused with 409 <c>player_has_characters</c>: the characters are moved or
    /// deleted first, deliberately.
    /// </summary>
    Task<AppResult> DeleteAsync(Guid playerId, Guid worldId, WorldRole actingRole, CancellationToken ct);

    /// <summary>
    /// The acting member takes an unlinked player as themselves: every character moves to
    /// their own player and the unlinked one is gone. Not Observers.
    /// </summary>
    Task<AppResult<Player>> ClaimAsync(Guid playerId, Guid worldId, Guid actingUserId, WorldRole actingRole, CancellationToken ct);

    /// <summary>The same as <see cref="ClaimAsync"/>, done by a GM on a member's behalf.</summary>
    Task<AppResult<Player>> LinkAsync(Guid playerId, Guid worldId, Guid worldMemberId, WorldRole actingRole, CancellationToken ct);
}
