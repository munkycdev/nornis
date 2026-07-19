using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

public interface IStorylineContinuityService
{
    /// <summary>
    /// Deterministic staleness read for the caller's visible storylines: which Active
    /// storylines have gone quiet (no development in the configured number of sessions) and
    /// which are unanchored (never advanced by a dated session). No AI, no cost.
    /// </summary>
    Task<AppResult<StorylineContinuityReport>> GetContinuityReportAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct);
}
