using Microsoft.Extensions.Logging;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Notifications;

/// <summary>Decides whether an extraction finishing is worth interrupting someone for.</summary>
public interface IExtractionNotifier
{
    Task NotifyExtractionFinishedAsync(
        Guid sourceId, Guid worldId, bool succeeded, int proposalCount, CancellationToken ct = default);
}

/// <summary>
/// The volume policy lives here, and it is the whole reason this class exists rather than a
/// bare call to the sender.
///
/// A source finishing extraction is only worth a notification when nobody is watching it happen.
/// During a paced walk — a campaign extraction or a replay — every note finishing is expected,
/// and announcing each one turns a ninety-note run into ninety buzzes. So a success is announced
/// only when the source is NOT the cursor of an active walk; the walk announces itself once, at
/// the end.
///
/// A failure is always announced. It stops a walk dead, and the whole point of being notified is
/// to learn about the thing that needs you.
/// </summary>
public class ExtractionNotifier : IExtractionNotifier
{
    private readonly ISourceRepository _sources;
    private readonly IImportSessionRepository _importSessions;
    private readonly IExtractionReplayRepository _replays;
    private readonly INotificationSender _sender;
    private readonly ILogger<ExtractionNotifier> _logger;

    public ExtractionNotifier(
        ISourceRepository sources,
        IImportSessionRepository importSessions,
        IExtractionReplayRepository replays,
        INotificationSender sender,
        ILogger<ExtractionNotifier> logger)
    {
        _sources = sources;
        _importSessions = importSessions;
        _replays = replays;
        _sender = sender;
        _logger = logger;
    }

    public async Task NotifyExtractionFinishedAsync(
        Guid sourceId, Guid worldId, bool succeeded, int proposalCount, CancellationToken ct = default)
    {
        try
        {
            var source = await _sources.GetByIdAsync(sourceId, ct);
            if (source is null)
            {
                return;
            }

            var title = Shorten(source.Title, 60);

            if (!succeeded)
            {
                await _sender.NotifyUserAsync(source.CreatedByUserId, new NotificationMessage(
                    Title: "Extraction failed",
                    Body: $"Nornis could not read “{title}”. It is waiting for a retry.",
                    Url: $"/sources/{sourceId}",
                    Tag: $"extraction-{sourceId}"), ct);
                return;
            }

            if (await IsPartOfAWalkAsync(sourceId, worldId, ct))
            {
                // Expected progress inside a run someone started. The run speaks for itself.
                return;
            }

            var body = proposalCount == 0
                ? $"Nornis read “{title}” and found nothing new to add."
                : $"“{title}” is ready — {proposalCount} proposal{(proposalCount == 1 ? "" : "s")} to review.";

            await _sender.NotifyUserAsync(source.CreatedByUserId, new NotificationMessage(
                Title: "Extraction finished",
                Body: body,
                Url: proposalCount == 0 ? $"/sources/{sourceId}" : "/review",
                Tag: $"extraction-{sourceId}"), ct);
        }
        catch (Exception ex)
        {
            // Never let telling someone about the work break the work.
            _logger.LogWarning(ex,
                "Could not raise an extraction notification. SourceId={SourceId}, WorldId={WorldId}",
                sourceId, worldId);
        }
    }

    /// <summary>
    /// Is this source the note a paced run is currently on? Only the cursor counts: a source
    /// that merely appears somewhere in a backlog is not being watched right now.
    /// </summary>
    private async Task<bool> IsPartOfAWalkAsync(Guid sourceId, Guid worldId, CancellationToken ct)
    {
        var replay = await _replays.GetActiveByWorldAsync(worldId, ct);
        if (replay?.CurrentSourceId == sourceId)
        {
            return true;
        }

        var session = await _importSessions.GetNonTerminalByWorldAsync(worldId, ct);
        return session is not null && session.Items.Any(i => i.SourceId == sourceId && i.DispatchedAt is not null);
    }

    private static string Shorten(string value, int max) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";
}
