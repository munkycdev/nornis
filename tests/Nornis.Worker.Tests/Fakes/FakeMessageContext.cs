namespace Nornis.Worker.Tests.Fakes;

/// <summary>
/// Fakes the message completion/abandonment operations that would normally
/// be performed via <see cref="Azure.Messaging.ServiceBus.ProcessMessageEventArgs"/>.
/// This allows unit tests to verify message disposition without requiring
/// real Azure Service Bus infrastructure.
/// </summary>
public sealed class FakeMessageContext
{
    public FakeMessageContext(string messageBody)
    {
        MessageBody = messageBody;
    }

    public string MessageBody { get; }
    public bool WasCompleted { get; private set; }
    public bool WasAbandoned { get; private set; }

    /// <summary>How many times this message has been delivered — drives the redelivery backoff.
    /// Defaults to the last allowed delivery so tests wait zero by default; set it lower to
    /// exercise a real backoff.</summary>
    public long DeliveryCount { get; init; } = RedeliveryBackoff.QueueMaxDeliveryCount;

    /// <summary>The backoff actually applied before abandoning, so tests can assert the handler
    /// backs off rather than releasing the message instantly.</summary>
    public TimeSpan? BackoffApplied { get; private set; }

    public void RecordBackoff(TimeSpan delay) => BackoffApplied = delay;

    public Task CompleteMessageAsync(CancellationToken cancellationToken = default)
    {
        WasCompleted = true;
        return Task.CompletedTask;
    }

    public Task AbandonMessageAsync(CancellationToken cancellationToken = default)
    {
        WasAbandoned = true;
        return Task.CompletedTask;
    }
}
