using Nornis.Domain.Entities;

namespace Nornis.Domain.Repositories;

/// <summary>A user's sign-ins and their one outstanding link code (feature 33).</summary>
public interface IUserSignInRepository
{
    /// <summary>The user's sign-ins, oldest first.</summary>
    Task<IReadOnlyList<UserSignIn>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserSignIn?> GetBySubjectAsync(string subject, CancellationToken cancellationToken = default);

    /// <summary>Adds a sign-in. A subject that is already taken fails on the unique index.</summary>
    Task AddAsync(UserSignIn signIn, CancellationToken cancellationToken = default);

    /// <summary>Deletes a sign-in. One that is already gone is left gone.</summary>
    Task DeleteAsync(Guid signInId, CancellationToken cancellationToken = default);

    /// <summary>Sets the user's link code, replacing the one they had.</summary>
    Task SetLinkCodeAsync(Guid userId, string codeHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and returns the link code with this hash, expired or not, so a code is spent the
    /// first time anyone presents it. Null when no code has it.
    /// </summary>
    Task<SignInLinkCode?> TakeLinkCodeAsync(string codeHash, CancellationToken cancellationToken = default);
}
