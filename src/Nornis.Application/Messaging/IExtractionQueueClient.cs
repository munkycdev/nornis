namespace Nornis.Application.Messaging;

public interface IExtractionQueueClient
{
    Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct, ExtractionKind kind = ExtractionKind.Extraction);
}
