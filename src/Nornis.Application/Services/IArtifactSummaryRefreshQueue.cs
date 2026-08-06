using Microsoft.Extensions.Logging;
using Nornis.Application.Messaging;

namespace Nornis.Application.Services;

/// <summary>
/// Requests worker-side summary regeneration for artifacts whose accepted content changed.
/// Never throws: summary freshness is best-effort, and an accept must not fail — or even
/// report failure — because the queue hiccuped. A missed request heals on the next accept
/// that touches the artifact. Same contract, and same required-with-NoOp registration
/// pattern, as <see cref="IExtractionReplayAdvancer"/>.
/// </summary>
public interface IArtifactSummaryRefreshQueue
{
    Task TryEnqueueAsync(Guid worldId, IReadOnlyCollection<Guid> artifactIds, CancellationToken ct);
}

/// <summary>For hosts and tests with genuinely no queue — the explicit "not wired" choice.</summary>
public sealed class NoOpArtifactSummaryRefreshQueue : IArtifactSummaryRefreshQueue
{
    public static readonly NoOpArtifactSummaryRefreshQueue Instance = new();

    private NoOpArtifactSummaryRefreshQueue()
    {
    }

    public Task TryEnqueueAsync(Guid worldId, IReadOnlyCollection<Guid> artifactIds, CancellationToken ct) =>
        Task.CompletedTask;
}

public class ArtifactSummaryRefreshQueue : IArtifactSummaryRefreshQueue
{
    private readonly IExtractionQueueClient _queueClient;
    private readonly ILogger<ArtifactSummaryRefreshQueue> _logger;

    public ArtifactSummaryRefreshQueue(
        IExtractionQueueClient queueClient,
        ILogger<ArtifactSummaryRefreshQueue> logger)
    {
        _queueClient = queueClient;
        _logger = logger;
    }

    public async Task TryEnqueueAsync(Guid worldId, IReadOnlyCollection<Guid> artifactIds, CancellationToken ct)
    {
        // One RequestedAt for the whole accept: these requests are one event, and the
        // staleness gate should treat them as one.
        var requestedAt = DateTimeOffset.UtcNow;

        foreach (var artifactId in artifactIds)
        {
            try
            {
                await _queueClient.SendSummaryRefreshAsync(artifactId, worldId, requestedAt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not enqueue a summary refresh; the next accept touching the artifact re-requests it. ArtifactId={ArtifactId}, WorldId={WorldId}",
                    artifactId, worldId);
            }
        }
    }
}
