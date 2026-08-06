using Nornis.Application.Services;

namespace Nornis.Application.Tests.Fakes;

public class FakeArtifactSummaryRefreshQueue : IArtifactSummaryRefreshQueue
{
    public List<(Guid WorldId, IReadOnlyList<Guid> ArtifactIds)> Requests { get; } = [];

    public Task TryEnqueueAsync(Guid worldId, IReadOnlyCollection<Guid> artifactIds, CancellationToken ct)
    {
        Requests.Add((worldId, artifactIds.ToList()));
        return Task.CompletedTask;
    }
}
