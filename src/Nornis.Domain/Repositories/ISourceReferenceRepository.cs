using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface ISourceReferenceRepository
{
    Task<SourceReference> CreateAsync(SourceReference reference, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceReference>> ListByTargetAsync(SourceReferenceTargetType targetType, Guid targetId, CancellationToken cancellationToken = default);
}
