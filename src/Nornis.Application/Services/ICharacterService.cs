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

    Task<AppResult> DeleteAsync(Guid characterId, Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
