using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Nornis.Application.Messaging;

namespace Nornis.Infrastructure.Messaging;

public class ServiceBusExtractionQueueClient : IExtractionQueueClient, IAsyncDisposable
{
    // Public so the API's service-bus status probe names the same queue this sends to,
    // rather than a copy that can drift — see ServiceBusLibraryIndexingQueueClient.
    public const string QueueName = "source-extraction";

    private readonly ServiceBusClient _serviceBusClient;

    /// <summary>
    /// One sender for the client's lifetime. <see cref="ServiceBusSender"/> is thread-safe and
    /// designed to be reused; creating one per message opened and tore down an AMQP link on every
    /// send, paying a link-attach round trip inside the user's request — and once per note during
    /// a bulk import. Lazy rather than constructor-created so construction stays free of I/O.
    /// </summary>
    private readonly Lazy<ServiceBusSender> _sender;

    public ServiceBusExtractionQueueClient(ServiceBusClient serviceBusClient)
    {
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _sender = new Lazy<ServiceBusSender>(() => _serviceBusClient.CreateSender(QueueName));
    }

    public async Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct, ExtractionKind kind = ExtractionKind.Extraction)
    {
        var message = new ExtractionMessage(sourceId, worldId, kind);
        var json = JsonSerializer.Serialize(message);

        var serviceBusMessage = new ServiceBusMessage(json)
        {
            ContentType = "application/json"
        };

        await _sender.Value.SendMessageAsync(serviceBusMessage, ct);
    }

    public async ValueTask DisposeAsync()
    {
        // Registered as a singleton, so the container disposes this at shutdown. Guard on
        // IsValueCreated or disposal would construct the very sender it is trying to release.
        if (_sender.IsValueCreated)
        {
            await _sender.Value.DisposeAsync();
        }

        GC.SuppressFinalize(this);
    }
}
