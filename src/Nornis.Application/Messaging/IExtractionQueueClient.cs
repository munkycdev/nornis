namespace Nornis.Application.Messaging;

public interface IExtractionQueueClient
{
    Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct, ExtractionKind kind = ExtractionKind.Extraction);

    /// <summary>Requests a worker-side regeneration of one artifact's summary.</summary>
    Task SendSummaryRefreshAsync(Guid artifactId, Guid worldId, DateTimeOffset requestedAt, CancellationToken ct);
}
