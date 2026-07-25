using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IExtractionReplayRepository
{
    Task<ExtractionReplay> CreateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default);

    /// <summary>The world's Active replay, or null. At most one exists at a time.</summary>
    Task<ExtractionReplay?> GetActiveByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task<ExtractionReplay> UpdateAsync(ExtractionReplay replay, CancellationToken cancellationToken = default);
}
