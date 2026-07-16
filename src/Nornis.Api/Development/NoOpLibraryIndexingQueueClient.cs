using Nornis.Application.Messaging;

namespace Nornis.Api.Development;

/// <summary>
/// No-op library-indexing queue client for local development without Azure Service Bus.
/// Documents will sit in Indexing status until a real worker picks them up.
/// </summary>
public class NoOpLibraryIndexingQueueClient : ILibraryIndexingQueueClient
{
    private readonly ILogger<NoOpLibraryIndexingQueueClient> _logger;

    public NoOpLibraryIndexingQueueClient(ILogger<NoOpLibraryIndexingQueueClient> logger)
    {
        _logger = logger;
    }

    public Task SendIndexingMessageAsync(Guid documentId, Guid worldId, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[DEV] Library indexing message skipped (no Service Bus). DocumentId={DocumentId}, WorldId={WorldId}.",
            documentId,
            worldId);

        return Task.CompletedTask;
    }
}
