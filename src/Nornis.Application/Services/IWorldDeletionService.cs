using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

/// <summary>
/// Permanently deletes a world and everything in it. Separate from <see cref="IWorldService"/>
/// because it depends on blob storage, whose DI registration throws when unconfigured —
/// resolve this only on the delete endpoint so the rest of the world surface stays up.
/// </summary>
public interface IWorldDeletionService
{
    Task<AppResult<bool>> DeleteAsync(DeleteWorldCommand command, CancellationToken ct);
}
