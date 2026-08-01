using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IExtractionReplayRepository
{
    /// <summary>
    /// Persists a new replay, or returns null when the world already has an Active one —
    /// a filtered unique index holds that invariant, so a check-then-create race resolves
    /// here rather than as a 500. Callers map null to the same conflict the check returns.
    /// </summary>
    Task<ExtractionReplay?> CreateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default);

    /// <summary>The world's Active replay, or null. At most one exists at a time.</summary>
    Task<ExtractionReplay?> GetActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<ExtractionReplay> UpdateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default);
}
