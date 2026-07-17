using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IUserRepository
{
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<User?> GetByAuth0SubjectIdAsync(string auth0SubjectId, CancellationToken cancellationToken = default);

    /// <summary>All users, ordered by username — feeds the add-member picker.</summary>
    Task<IReadOnlyList<User>> ListAsync(CancellationToken cancellationToken = default);

    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
}
