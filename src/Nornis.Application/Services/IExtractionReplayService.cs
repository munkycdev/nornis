using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// The narrow seam the review and extraction pipelines call when a source's extraction
/// batch reaches its terminal reviewed state. Implementations must never throw — an
/// advance failure must not fail the review action or extraction that triggered it.
/// </summary>
public interface IExtractionReplayAdvancer
{
    /// <summary>Advances the world's active replay if <paramref name="sourceId"/> is its
    /// cursor: reprocesses the next eligible timeline source, or completes the replay when
    /// none remain. A no-op when no replay is active or the source is not the cursor.</summary>
    Task TryAdvanceAsync(Guid worldId, Guid sourceId, CancellationToken ct);
}

public interface IExtractionReplayService : IExtractionReplayAdvancer
{
    /// <summary>How many sources a replay starting at this source would walk (the source
    /// itself plus every eligible timeline source after it). Same gate as StartAsync.</summary>
    Task<AppResult<int>> CountFromAsync(
        Guid worldId, Guid startSourceId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Starts a replay from the given source: creates the run, then cascades and
    /// requeues the starting source. GM only; one active replay per world.</summary>
    Task<AppResult<ExtractionReplayInfo>> StartAsync(
        Guid worldId, Guid startSourceId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>The world's active replay, or Success(null) when none is running. GM only.</summary>
    Task<AppResult<ExtractionReplayInfo?>> GetActiveAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);

    /// <summary>Cancels the active replay. The in-flight source finishes its normal
    /// lifecycle; the walk simply stops advancing. GM only.</summary>
    Task<AppResult<ExtractionReplayInfo>> CancelAsync(
        Guid worldId, Guid actingUserId, WorldRole actingUserRole, CancellationToken ct);
}
