namespace Nornis.Domain.Repositories;

/// <summary>
/// Write side lives in the worker, read side in the API — the two never share a process,
/// which is the whole point: the table is the channel between them.
/// </summary>
public interface IWorkerHeartbeatRepository
{
    /// <summary>Records that <paramref name="workerName"/> was alive at <paramref name="beatAt"/>.</summary>
    Task BeatAsync(string workerName, DateTimeOffset beatAt, CancellationToken cancellationToken = default);

    /// <summary>Null when the host has never beaten — a fresh database, or a worker that has never started.</summary>
    Task<DateTimeOffset?> GetLastBeatAsync(string workerName, CancellationToken cancellationToken = default);
}
