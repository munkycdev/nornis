using Nornis.Application.Errors;
using Nornis.Application.Models;

namespace Nornis.Application.Services;

public interface IContinuityAuditService
{
    /// <summary>
    /// Runs an AI continuity assessment over the campaign's full (GM-scoped) record, persists the
    /// assessment and its validated findings, and returns them with the effective score.
    /// </summary>
    Task<AppResult<ContinuityAssessment>> RunAssessmentAsync(Guid campaignId, Guid? userId, CancellationToken ct);

    /// <summary>
    /// Returns the latest assessment for a campaign with its findings and a freshly-computed
    /// effective score. When the campaign has never been assessed, returns a has-data:false result.
    /// </summary>
    Task<AppResult<ContinuityAssessment>> GetLatestAsync(Guid campaignId, CancellationToken ct);

    /// <summary>Transitions an Open finding to Dismissed. No-op-safe for already-dismissed findings.</summary>
    Task<AppResult<ContinuityFindingView>> DismissFindingAsync(Guid campaignId, Guid findingId, CancellationToken ct);
}
