using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface IHealthService
{
    /// <summary>
    /// Computes a heuristic continuity-health score for the world from its artifacts, facts,
    /// relationships, and source references. Deterministic and cheap — recomputed on demand.
    /// </summary>
    Task<AppResult<WorldHealth>> GetHealthAsync(Guid worldId, CancellationToken ct);
}
