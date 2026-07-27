using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Nornis.Application.Messaging;

namespace Nornis.Infrastructure.Messaging;

public class ServiceBusLibraryIndexingQueueClient : ILibraryIndexingQueueClient, IAsyncDisposable
{
    public const string QueueName = "library-indexing";

    private readonly ServiceBusClient _serviceBusClient;

    /// <summary>See the note on <see cref="ServiceBusExtractionQueueClient"/> — one reused,
    /// thread-safe sender instead of an AMQP link cycle per message.</summary>
    private readonly Lazy<ServiceBusSender> _sender;

    public ServiceBusLibraryIndexingQueueClient(ServiceBusClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _sender = new Lazy<ServiceBusSender>(() => _serviceBusClient.CreateSender(QueueName));
    }

    public async Task SendIndexingMessageAsync(Guid documentId, Guid worldId, CancellationToken ct = default)
    {
        var message = new LibraryIndexingMessage(documentId, worldId);
        var json = JsonSerializer.Serialize(message);

        var serviceBusMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json"
        };

        await _sender.Value.SendMessageAsync(serviceBusMessage, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sender.IsValueCreated)
        {
            await _sender.Value.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
