using Nornis.Domain.Entities;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Tests.Fakes;

public class InMemoryUserRepository : IUserRepository
{
    private readonly List<User> _users = [];

    public IReadOnlyList<User> Users => _users.AsReadOnly();

    public Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        _users.Add(user);
        return Task.FromResult(user);
    }

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = _users.FirstOrDefault(u => u.Id == id);
        return Task.FromResult(user);
    }

    public Task<User?> GetByAuth0SubjectIdAsync(string auth0SubjectId, CancellationToken cancellationToken = default)
    {
        var user = _users.FirstOrDefault(u => u.Auth0SubjectId == auth0SubjectId);
        return Task.FromResult(user);
    }

    /// <summary>Memberships this fake knows about, seeded with <see cref="SeedMembership"/>.</summary>
    private readonly HashSet<(Guid WorldId, Guid UserId)> _memberships = [];

    public void SeedMembership(Guid worldId, Guid userId) => _memberships.Add((worldId, userId));

    /// <summary>
    /// Mirrors the real query's shape. The real join is covered against a provider in
    /// <c>UserRepositoryAddableTests</c> — this exists so Application-layer callers have something
    /// to run against, not to be the thing that proves the exclusion works.
    /// </summary>
    public Task<IReadOnlyList<User>> ListAddableToWorldAsync(
        Guid worldId, string? search, int limit, CancellationToken cancellationToken = default)
    {
        var users = _users
            .Where(u => !_memberships.Contains((worldId, u.Id)))
            .Where(u => string.IsNullOrWhiteSpace(search) || u.Username.Contains(search.Trim()))
            .OrderBy(u => u.Username)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<User>>(users.AsReadOnly());
    }

    public Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        var index = _users.FindIndex(u => u.Id == user.Id);
        if (index >= 0)
        {
            _users[index] = user;
        }
        return Task.FromResult(user);
    }
}
