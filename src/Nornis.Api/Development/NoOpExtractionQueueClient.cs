using Nornis.Application.Messaging;

namespace Nornis.Api.Development;

/// <summary>
/// No-op extraction queue client for local development without Azure Service Bus.
/// Logs the message that would have been sent but does not enqueue anything.
/// Sources will be created with Queued status but won't be processed until
/// a real Service Bus is connected.
/// </summary>
public class NoOpExtractionQueueClient : IExtractionQueueClient
{
    private readonly ILogger<NoOpExtractionQueueClient> _logger;

    public NoOpExtractionQueueClient(ILogger<NoOpExtractionQueueClient> logger)
    {
        _logger = logger;
    }

    public Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct, ExtractionKind kind = ExtractionKind.Extraction)
    {
        _logger.LogWarning(
            "[DEV] Extraction message skipped (no Service Bus). SourceId={SourceId}, WorldId={WorldId}. " +
            "Source will remain in Queued status until a worker processes it.",
            sourceId,
            worldId);

        return Task.CompletedTask;
    }
}
