using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;

namespace Nornis.Application.Services;

public interface IWorldService
{
    Task<AppResult<World>> CreateAsync(CreateWorldCommand command, CancellationToken ct);
    Task<AppResult<World>> GetByIdAsync(Guid worldId, Guid requestingUserId, CancellationToken ct);
    Task<AppResult<World>> UpdateAsync(UpdateWorldCommand command, CancellationToken ct);
    Task<AppResult<IReadOnlyList<WorldWithRoleDto>>> ListForUserAsync(Guid userId, CancellationToken ct);
}
