using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Nornis.Application.Messaging;

namespace Nornis.Infrastructure.Messaging;

public class ServiceBusExtractionQueueClient : IExtractionQueueClient
{
    private const string QueueName = "source-extraction";

    private readonly ServiceBusClient _serviceBusClient;

    public ServiceBusExtractionQueueClient(ServiceBusClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
    }

    public async Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct)
    {
        var message = new ExtractionMessage(sourceId, worldId);
        var json = JsonSerializer.Serialize(message);

        await using var sender = _serviceBusClient.CreateSender(QueueName);
        var serviceBusMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json"
        };

        await sender.SendMessageAsync(serviceBusMessage, ct);
    }
}
