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
