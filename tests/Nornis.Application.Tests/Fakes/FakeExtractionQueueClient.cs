using Nornis.Application.Messaging;

namespace Nornis.Application.Tests.Fakes;

public class FakeExtractionQueueClient : IExtractionQueueClient
{
    private readonly List<(Guid SourceId, Guid WorldId, ExtractionKind Kind)> _sentMessages = [];
    private bool _shouldFail;

    public IReadOnlyList<(Guid SourceId, Guid WorldId, ExtractionKind Kind)> SentMessages => _sentMessages.AsReadOnly();

    /// <summary>Invoked at the moment of send — lets tests observe state mid-enqueue.</summary>
    public Action<Guid, Guid>? OnSend { get; set; }

    public void ConfigureToFail(bool shouldFail = true)
    {
        _shouldFail = shouldFail;
    }

    public Task SendExtractionMessageAsync(Guid sourceId, Guid worldId, CancellationToken ct, ExtractionKind kind = ExtractionKind.Extraction)
    {
        OnSend?.Invoke(sourceId, worldId);

        if (_shouldFail)
        {
            throw new InvalidOperationException("Simulated queue failure.");
        }

        _sentMessages.Add((sourceId, worldId, kind));
        return Task.CompletedTask;
    }
}
