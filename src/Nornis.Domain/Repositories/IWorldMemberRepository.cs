using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IWorldMemberRepository
{
    Task<WorldMember> CreateAsync(WorldMember member, CancellationToken cancellationToken = default);

    Task<WorldMember?> GetByWorldAndUserAsync(Guid worldId, Guid userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldMember>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    Task RemoveAsync(WorldMember member, CancellationToken cancellationToken = default);

    Task<WorldMember> UpdateAsync(WorldMember member, CancellationToken cancellationToken = default);

    Task<int> CountByRoleAsync(Guid worldId, WorldRole role, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldMember>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
