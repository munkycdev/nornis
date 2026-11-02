using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

public interface IUserRepository
{
    /// <summary>
    /// Creates <paramref name="user"/> with <paramref name="subject"/> as its one sign-in, in one
    /// save. A subject another user already owns fails on the unique index (a create race).
    /// </summary>
    Task<User> CreateAsync(User user, string subject, CancellationToken cancellationToken = default);

    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The user one of whose sign-ins is <paramref name="subject"/>, or null.</summary>
    Task<User?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>
    /// Users who are not yet members of <paramref name="worldId"/>, ordered by username — the
    /// candidates for that world's add-member picker.
    ///
    /// <para>This deliberately has no "list every user" sibling. The picker was the only caller of
    /// one, and it exposed the whole user directory to any authenticated caller; scoping the query
    /// to a world the caller demonstrably GMs is what keeps the directory from being enumerable.
    /// <paramref name="search"/> is matched against the username, and <paramref name="limit"/>
    /// caps the result so the response cannot grow with the user table.</para>
    /// </summary>
    Task<IReadOnlyList<User>> ListAddableToWorldAsync(
        Guid worldId, string? search, int limit, CancellationToken cancellationToken = default);

    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
}
