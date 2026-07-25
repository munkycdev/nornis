using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface ITutorialProgressRepository
{
    Task<IReadOnlyList<TutorialProgress>> ListAsync(Guid userId, Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>Records newly-completed steps. Callers pass only steps not already present;
    /// a concurrent duplicate insert is swallowed (completion is idempotent).</summary>
    Task AddRangeAsync(IReadOnlyList<TutorialProgress> entries, CancellationToken cancellationToken = default);
}
