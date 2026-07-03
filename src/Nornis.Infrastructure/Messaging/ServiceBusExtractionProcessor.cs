using Azure.Messaging.ServiceBus;

namespace Nornis.Infrastructure.Messaging;

/// <summary>
/// Wraps <see cref="ServiceBusProcessor"/> to provide message reception from the
/// source-extraction queue. Configures peek-lock mode, concurrency, prefetch,
/// and lock renewal from constructor parameters. Exposes lifecycle methods
/// suitable for BackgroundService start/stop.
/// </summary>
public sealed class ServiceBusExtractionProcessor : IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusProcessor _processor;

    public ServiceBusExtractionProcessor(
        string connectionString,
        string queueName,
        int maxConcurrentCalls,
        int prefetchCount,
        TimeSpan maxAutoLockRenewalDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        _client = new ServiceBusClient(connectionString);

        var options = new ServiceBusProcessorOptions
        {
            // Peek-lock is the default receive mode — messages remain on the queue
            // until explicitly completed or abandoned.
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            MaxConcurrentCalls = maxConcurrentCalls,
            PrefetchCount = prefetchCount,
            MaxAutoLockRenewalDuration = maxAutoLockRenewalDuration,
            AutoCompleteMessages = false
        };

        _processor = _client.CreateProcessor(queueName, options);
    }

    /// <summary>
    /// Registers the handler invoked for each received message.
    /// Must be set before calling <see cref="StartProcessingAsync"/>.
    /// </summary>
    public event Func<ProcessMessageEventArgs, Task> ProcessMessageAsync
    {
        add => _processor.ProcessMessageAsync += value;
        remove => _processor.ProcessMessageAsync -= value;
    }

    /// <summary>
    /// Registers the handler invoked when the processor encounters an error.
    /// Must be set before calling <see cref="StartProcessingAsync"/>.
    /// </summary>
    public event Func<ProcessErrorEventArgs, Task> ProcessErrorAsync
    {
        add => _processor.ProcessErrorAsync += value;
        remove => _processor.ProcessErrorAsync -= value;
    }

    /// <summary>
    /// Starts receiving messages from the queue. Call from
    /// <see cref="Microsoft.Extensions.Hosting.BackgroundService.ExecuteAsync"/>.
    /// </summary>
    public Task StartProcessingAsync(CancellationToken cancellationToken = default)
        => _processor.StartProcessingAsync(cancellationToken);

    /// <summary>
    /// Stops receiving messages. Call from
    /// <see cref="Microsoft.Extensions.Hosting.BackgroundService.StopAsync"/>.
    /// </summary>
    public Task StopProcessingAsync(CancellationToken cancellationToken = default)
        => _processor.StopProcessingAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _processor.DisposeAsync();
        await _client.DisposeAsync();
    }
}
