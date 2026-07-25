using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

/// <summary>Records TryAdvance calls from the review and extraction pipelines.</summary>
public class FakeExtractionReplayAdvancer : IExtractionReplayAdvancer
{
    public List<(Guid WorldId, Guid SourceId)> Calls { get; } = [];

    public Task TryAdvanceAsync(Guid worldId, Guid sourceId, CancellationToken ct)
    {
        Calls.Add((worldId, sourceId));
        return Task.CompletedTask;
    }
}
