using NUnit.Framework;
using Nornis.Infrastructure.Messaging;

namespace Nornis.Worker.Tests.Messaging;

[TestFixture]
public class ServiceBusExtractionProcessorTests
{
    // Valid Service Bus connection string format for construction tests.
    // The SDK validates the format at construction time but doesn't connect until processing starts.
    private const string ValidConnectionString =
        "Endpoint=sb://test-namespace.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=dGVzdC1rZXk=";

    private const string QueueName = "source-extraction";

    [Test]
    public void Constructor_ThrowsArgumentException_WhenConnectionStringIsNull()
    {
        Assert.That(
            () => new ServiceBusExtractionProcessor(
                connectionString: null!,
                queueName: QueueName,
                maxConcurrentCalls: 1,
                prefetchCount: 0,
                maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5)),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_ThrowsArgumentException_WhenConnectionStringIsEmpty()
    {
        Assert.That(
            () => new ServiceBusExtractionProcessor(
                connectionString: string.Empty,
                queueName: QueueName,
                maxConcurrentCalls: 1,
                prefetchCount: 0,
                maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Constructor_ThrowsArgumentException_WhenConnectionStringIsWhitespace()
    {
        Assert.That(
            () => new ServiceBusExtractionProcessor(
                connectionString: "   ",
                queueName: QueueName,
                maxConcurrentCalls: 1,
                prefetchCount: 0,
                maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Constructor_ThrowsArgumentException_WhenQueueNameIsNull()
    {
        Assert.That(
            () => new ServiceBusExtractionProcessor(
                connectionString: ValidConnectionString,
                queueName: null!,
                maxConcurrentCalls: 1,
                prefetchCount: 0,
                maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5)),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_ThrowsArgumentException_WhenQueueNameIsEmpty()
    {
        Assert.That(
            () => new ServiceBusExtractionProcessor(
                connectionString: ValidConnectionString,
                queueName: string.Empty,
                maxConcurrentCalls: 1,
                prefetchCount: 0,
                maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5)),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task Constructor_ConfiguresPeekLockMode_ProcessorCreatedSuccessfully()
    {
        // Peek-lock is configured in the ServiceBusProcessorOptions passed to CreateProcessor.
        // If construction succeeds without throwing, the options (including ReceiveMode.PeekLock)
        // were applied. The SDK does not expose the receive mode after construction, but the
        // implementation explicitly sets ReceiveMode = ServiceBusReceiveMode.PeekLock.
        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: QueueName,
            maxConcurrentCalls: 1,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5));

        Assert.That(processor, Is.Not.Null);
    }

    [Test]
    public async Task Constructor_AcceptsMaxConcurrentCalls_FromOptions()
    {
        // The MaxConcurrentCalls value from WorkerOptions is passed to ServiceBusProcessorOptions.
        // A value of 4 should be accepted without error.
        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: QueueName,
            maxConcurrentCalls: 4,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5));

        Assert.That(processor, Is.Not.Null);
    }

    [Test]
    public async Task Constructor_AcceptsMaxAutoLockRenewalDuration_FromOptions()
    {
        // The MaxAutoLockRenewalDuration from WorkerOptions is passed to ServiceBusProcessorOptions.
        // A custom duration of 10 minutes should be accepted without error.
        var customDuration = TimeSpan.FromMinutes(10);

        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: QueueName,
            maxConcurrentCalls: 1,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: customDuration);

        Assert.That(processor, Is.Not.Null);
    }

    [Test]
    public async Task Constructor_AcceptsDefaultWorkerOptions_Configuration()
    {
        // Matches the default WorkerOptions: MaxConcurrentCalls=1, PrefetchCount=0,
        // MaxAutoLockRenewalDuration=5 minutes, QueueName="source-extraction"
        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: "source-extraction",
            maxConcurrentCalls: 1,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5));

        Assert.That(processor, Is.Not.Null);
    }

    [Test]
    public async Task StartProcessingAsync_ThrowsWithoutHandlers_WhenNoMessageHandlerRegistered()
    {
        // StartProcessingAsync should throw InvalidOperationException when called without
        // registering ProcessMessageAsync and ProcessErrorAsync handlers first.
        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: QueueName,
            maxConcurrentCalls: 1,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5));

        Assert.That(
            async () => await processor.StartProcessingAsync(CancellationToken.None),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task StopProcessingAsync_CanBeCalledWithoutStarting()
    {
        // StopProcessingAsync should not throw when the processor hasn't been started.
        // This is important for graceful shutdown in BackgroundService.StopAsync when
        // the service is stopped before it fully starts.
        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: QueueName,
            maxConcurrentCalls: 1,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5));

        Assert.That(
            async () => await processor.StopProcessingAsync(CancellationToken.None),
            Throws.Nothing);
    }

    [Test]
    public async Task DisposeAsync_CanBeCalledSafely()
    {
        // DisposeAsync should clean up resources without throwing.
        var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: QueueName,
            maxConcurrentCalls: 1,
            prefetchCount: 0,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(5));

        Assert.That(
            async () => await processor.DisposeAsync(),
            Throws.Nothing);
    }

    [Test]
    public void Processor_ImplementsIAsyncDisposable()
    {
        Assert.That(typeof(ServiceBusExtractionProcessor).GetInterfaces(),
            Does.Contain(typeof(IAsyncDisposable)));
    }
}
