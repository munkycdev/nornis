using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface IHealthService
{
    /// <summary>
    /// Computes a heuristic continuity-health score for the campaign from its artifacts, facts,
    /// relationships, and source references. Deterministic and cheap — recomputed on demand.
    /// </summary>
    Task<AppResult<CampaignHealth>> GetHealthAsync(Guid campaignId, CancellationToken ct);
}
