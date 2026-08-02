using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IContinuityAuditService
{
    /// <summary>
    /// Runs an AI continuity assessment over the world's full (GM-scoped) record, persists the
    /// assessment and its validated findings, and returns them with the effective score.
    /// </summary>
    /// <para><paramref name="actingUserRole"/> is null for the background trigger, which runs
    /// on a schedule rather than for a user — the same shape as the nullable
    /// <paramref name="userId"/> beside it. Any non-null role must be GM.</para>
    Task<AppResult<ContinuityAssessment>> RunAssessmentAsync(
        Guid worldId, Guid? userId, WorldRole? actingUserRole, CancellationToken ct);

    /// <summary>
    /// Returns the latest assessment for a world with its findings and a freshly-computed
    /// effective score. When the world has never been assessed, returns a has-data:false result.
    /// </summary>
    Task<AppResult<ContinuityAssessment>> GetLatestAsync(
        Guid worldId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Transitions an Open finding to Dismissed. No-op-safe for already-dismissed findings.</summary>
    Task<AppResult<ContinuityFindingView>> DismissFindingAsync(
        Guid worldId, Guid findingId, WorldRole actingUserRole, CancellationToken ct);
}
