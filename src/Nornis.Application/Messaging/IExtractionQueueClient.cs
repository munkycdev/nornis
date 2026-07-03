namespace Nornis.Application.Messaging;

public interface IExtractionQueueClient
{
    Task SendExtractionMessageAsync(Guid sourceId, Guid campaignId, CancellationToken ct);
}
