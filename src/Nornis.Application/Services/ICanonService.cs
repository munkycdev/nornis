using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface ICanonService
{
    /// <summary>
    /// Builds the Canon view for a world: all facts and relationships visible to the
    /// requesting role, tagged with truth state and sorted most-recently-updated first.
    /// Hidden truth-state entries are returned only to GMs. Optionally filtered to a single
    /// truth state.
    /// </summary>
    Task<AppResult<IReadOnlyList<CanonEntry>>> GetCanonAsync(CanonQuery query, CancellationToken ct);
}
