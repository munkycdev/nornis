using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IConvergenceGaugeService
{
    /// <summary>
    /// Ranks a world's hidden material by how ready it is to be revealed. GM only, and
    /// read-only — it changes no visibility, writes no proposals, and reveals nothing. Each
    /// candidate carries the closure a reveal would require so the caller can hand it straight
    /// to <see cref="IRevealService.RevealAsync"/> with the GM confirming.
    /// </summary>
    Task<AppResult<ConvergenceGauge>> GetGaugeAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}
