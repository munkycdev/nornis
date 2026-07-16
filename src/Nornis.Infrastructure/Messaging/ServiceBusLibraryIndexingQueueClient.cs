using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Nornis.Application.Messaging;

namespace Nornis.Infrastructure.Messaging;

public class ServiceBusLibraryIndexingQueueClient : ILibraryIndexingQueueClient
{
    public const string QueueName = "library-indexing";

    private readonly ServiceBusClient _serviceBusClient;

    public ServiceBusLibraryIndexingQueueClient(ServiceBusClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
    }

    public async Task SendIndexingMessageAsync(Guid documentId, Guid worldId, CancellationToken ct = default)
    {
        var message = new LibraryIndexingMessage(documentId, worldId);
        var json = JsonSerializer.Serialize(message);

        await using var sender = _serviceBusClient.CreateSender(QueueName);
        var serviceBusMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json"
        };

        await sender.SendMessageAsync(serviceBusMessage, ct);
    }
}
