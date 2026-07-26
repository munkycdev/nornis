using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>
/// The world-scoped registry of dismissed continuity issues. Append-only: a dismissal is a
/// GM adjudication that outlives the assessment run that surfaced it.
/// </summary>
public interface IContinuityDismissalRepository
{
    Task<ContinuityDismissal> CreateAsync(
        ContinuityDismissal dismissal,
        CancellationToken cancellationToken = default);

    /// <summary>Every dismissal ever recorded for the world, oldest first.</summary>
    Task<IReadOnlyList<ContinuityDismissal>> ListByWorldAsync(
        Guid worldId,
        CancellationToken cancellationToken = default);
}
