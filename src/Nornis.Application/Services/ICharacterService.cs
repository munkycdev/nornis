using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface ICharacterService
{
    Task<AppResult<Character>> CreateAsync(CreateCharacterCommand command, CancellationToken ct);

    Task<AppResult<Character>> GetByIdAsync(Guid characterId, Guid worldId, CancellationToken ct);

    Task<AppResult<IReadOnlyList<Character>>> ListByWorldAsync(Guid worldId, CancellationToken ct);

    Task<AppResult<Character>> UpdateAsync(UpdateCharacterCommand command, CancellationToken ct);

    /// <summary>
    /// Transfers ownership of an existing character to the acting member — e.g. a player
    /// taking over a character the GM created or imported before they joined. Any
    /// non-Observer member may claim; tables are small and trusted, and ownership can
    /// always be claimed back.
    /// </summary>
    Task<AppResult<Character>> ClaimAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    Task<AppResult> DeleteAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
