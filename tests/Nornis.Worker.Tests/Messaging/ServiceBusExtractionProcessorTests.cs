using Nornis.Infrastructure.Messaging;
using NUnit.Framework;

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

    /// <summary>
    /// Every combination the worker actually constructs, in one test, because "it did not
    /// throw" is the only fact available and one test establishes it.
    ///
    /// <para>
    /// This was four tests — peek-lock mode, MaxConcurrentCalls, MaxAutoLockRenewalDuration,
    /// and the default set — each asserting <c>Is.Not.Null</c> on the processor. Their names
    /// claimed the options were applied; their bodies could not tell whether the constructor
    /// forwarded them or discarded them, because the SDK exposes none of it afterwards. Four
    /// tests reading as four verified options, over one unverifiable fact.
    /// </para>
    /// </summary>
    [TestCase(1, 0, 5, QueueName, Description = "the worker's defaults")]
    [TestCase(4, 0, 5, QueueName, Description = "raised concurrency")]
    [TestCase(1, 0, 10, QueueName, Description = "extended lock renewal, as library indexing uses")]
    [TestCase(1, 0, 5, "source-extraction", Description = "the real queue name")]
    public async Task Constructor_AcceptsEveryOptionSetTheWorkerUses(
        int maxConcurrentCalls, int prefetchCount, int lockRenewalMinutes, string queueName)
    {
        await using var processor = new ServiceBusExtractionProcessor(
            connectionString: ValidConnectionString,
            queueName: queueName,
            maxConcurrentCalls: maxConcurrentCalls,
            prefetchCount: prefetchCount,
            maxAutoLockRenewalDuration: TimeSpan.FromMinutes(lockRenewalMinutes));

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
