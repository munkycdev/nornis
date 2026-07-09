using Nornis.Application.Messaging;

namespace Nornis.Api.Tests.Infrastructure;

/// <summary>
/// A fake implementation of IExtractionQueueClient for integration testing.
/// Records all sent messages and can be configured to throw to simulate queue failures.
/// </summary>
public class FakeExtractionQueueClient : IExtractionQueueClient
{
    private readonly List<(Guid SourceId, Guid WorldId)> _sentMessages = [];
    private bool _shouldFail;

    /// <summary>
    /// All extraction messages sent through this client.
    /// </summary>
    public IReadOnlyList<(Guid SourceId, Guid WorldId)> SentMessages => _sentMessages.AsReadOnly();

    /// <summary>
    /// Configures the client to throw an InvalidOperationException on the next send,
    /// simulating an Azure Service Bus failure for 502 Bad Gateway tests.
    /// </summary>
    public void ConfigureToFail(bool shouldFail = true)
    {
        _shouldFail = shouldFail;
    }

    /// <summary>
    /// Resets the client state — clears sent messages and removes failure configuration.
    /// </summary>
    public void Reset()
    {
        _sentMessages.Clear();
        _shouldFail = false;
    }

    public Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct)
    {
        if (_shouldFail)
        {
            throw new InvalidOperationException("Simulated queue failure.");
        }

        _sentMessages.Add((sourceId, worldId));
        return Task.CompletedTask;
    }
}
