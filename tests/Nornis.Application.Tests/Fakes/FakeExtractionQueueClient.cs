using Nornis.Application.Messaging;

namespace Nornis.Application.Tests.Fakes;

public class FakeExtractionQueueClient : IExtractionQueueClient
{
    private readonly List<(Guid SourceId, Guid WorldId)> _sentMessages = [];
    private bool _shouldFail;

    public IReadOnlyList<(Guid SourceId, Guid WorldId)> SentMessages => _sentMessages.AsReadOnly();

    public void ConfigureToFail(bool shouldFail = true)
    {
        _shouldFail = shouldFail;
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
