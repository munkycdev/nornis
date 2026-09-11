namespace Nornis.Domain.Repositories;

/// <summary>
/// Assigns slugs to rows written before slugs existed. Idempotent: a row with a slug is never
/// touched, so running it on every host start costs one cheap query per kind once the backlog
/// is gone. Returns how many rows were assigned, for the log line.
/// </summary>
public interface ISlugBackfiller
{
    Task<int> BackfillAsync(CancellationToken cancellationToken = default);
}
