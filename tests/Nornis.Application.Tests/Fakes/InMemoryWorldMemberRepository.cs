using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryWorldMemberRepository : IWorldMemberRepository
{
    private readonly List<WorldMember> _members = [];

    public IReadOnlyList<WorldMember> Members => _members.AsReadOnly();

    public Task<WorldMember> CreateAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        _members.Add(member);
        return Task.FromResult(member);
    }

    public Task<WorldMember?> GetByWorldAndUserAsync(Guid worldId, Guid userId, CancellationToken cancellationToken = default)
    {
        var member = _members.FirstOrDefault(m => m.WorldId == worldId && m.UserId == userId);
        return Task.FromResult(member);
    }

    public Task<IReadOnlyList<WorldMember>> ListByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        var members = _members.Where(m => m.WorldId == worldId).ToList();
        return Task.FromResult<IReadOnlyList<WorldMember>>(members.AsReadOnly());
    }

    public Task RemoveAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        _members.RemoveAll(m => m.Id == member.Id);
        return Task.CompletedTask;
    }

    public Task<WorldMember> UpdateAsync(WorldMember member, CancellationToken cancellationToken = default)
    {
        var index = _members.FindIndex(m => m.Id == member.Id);
        if (index >= 0)
        {
            _members[index] = member;
        }
        return Task.FromResult(member);
    }

    public Task<int> CountByRoleAsync(Guid worldId, WorldRole role, CancellationToken cancellationToken = default)
    {
        var count = _members.Count(m => m.WorldId == worldId && m.Role == role);
        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<WorldMember>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var members = _members.Where(m => m.UserId == userId).ToList();
        return Task.FromResult<IReadOnlyList<WorldMember>>(members.AsReadOnly());
    }
}
