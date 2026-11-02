using Nornis.Domain.Entities;

namespace Nornis.Application.Bibliotaph;

/// <summary>
/// Access tokens for the Bibliotaph API, from a connection's refresh token. The only thing that
/// talks to Auth0's token endpoint for Bibliotaph. Null means Auth0 refused the grant
/// (<c>invalid_grant</c>); a network failure throws.
/// </summary>
public interface IBibliotaphTokenSource
{
    Task<string?> GetAccessTokenAsync(BibliotaphConnection connection, CancellationToken ct);

    /// <summary>Forgets any cached access token for the connection, after Bibliotaph refused one.</summary>
    void Forget(Guid connectionId);
}

/// <summary>
/// Protects refresh tokens with the Bibliotaph Data Protection ring, which the Web can't read.
/// </summary>
public interface IBibliotaphTokenProtector
{
    string Protect(string refreshToken);

    /// <summary>Null when the ciphertext can't be read (a lost ring, or tampering).</summary>
    string? Unprotect(string protectedRefreshToken);

    /// <summary>Protects a connect flow's state, under a purpose of its own, as a URL-safe string.</summary>
    string ProtectState(string state);

    /// <summary>Null when the state can't be read: tampered, or protected by another ring.</summary>
    string? UnprotectState(string protectedState);
}
