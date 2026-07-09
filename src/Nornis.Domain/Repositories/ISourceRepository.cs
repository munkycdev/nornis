using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface ISourceRepository
{
    Task<Source> CreateAsync(Source source, CancellationToken cancellationToken = default);

    Task<Source?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Source>> ListByWorldAsync(Guid worldId, VisibilityScope? visibility = null, CancellationToken cancellationToken = default);

    Task UpdateProcessingStatusAsync(Guid id, SourceProcessingStatus status, CancellationToken cancellationToken = default);

    Task<Source> UpdateAsync(Source source, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
