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

    public Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = _users.OrderBy(u => u.Username).ToList();
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
