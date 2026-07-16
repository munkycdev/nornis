namespace Nornis.Application.Messaging;

/// <summary>Queue message: chunk + embed one library document.</summary>
public record LibraryIndexingMessage(Guid DocumentId, Guid WorldId);

/// <summary>Producer for the library-indexing queue (mirrors <see cref="IExtractionQueueClient"/>).</summary>
public interface ILibraryIndexingQueueClient
{
    Task SendIndexingMessageAsync(Guid documentId, Guid worldId, CancellationToken ct = default);
}
