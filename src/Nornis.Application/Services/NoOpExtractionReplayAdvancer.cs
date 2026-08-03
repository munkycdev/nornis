namespace Nornis.Application.Services;

/// <summary>
/// Advances nothing, for callers with no replay walk to advance. Follows
/// <see cref="Ai.NoOpWorldNameGenerator"/>: the absence of the feature is stated by the
/// registration rather than inferred from a null field at each use site.
/// </summary>
public sealed class NoOpExtractionReplayAdvancer : IExtractionReplayAdvancer
{
    public static readonly NoOpExtractionReplayAdvancer Instance = new();

    public Task TryAdvanceAsync(Guid worldId, Guid sourceId, CancellationToken ct) =>
        Task.CompletedTask;
}
