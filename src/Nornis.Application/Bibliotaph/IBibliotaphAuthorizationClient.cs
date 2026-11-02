namespace Nornis.Application.Bibliotaph;

/// <summary>
/// Auth0's half of connecting: the authorization-code exchange for the Bibliotaph API, and
/// revoking a grant on disconnect. The exchange validates the ID token and returns its subject,
/// so the caller can refuse a grant for someone else.
/// </summary>
public interface IBibliotaphAuthorizationClient
{
    /// <summary>Null when Auth0 refused the code, or the ID token didn't validate. A network failure throws.</summary>
    Task<BibliotaphGrant?> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct);

    /// <summary>Best effort: a failure is logged, never thrown, because the grant is forgotten either way.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken ct);
}

/// <summary>A refresh token for the Bibliotaph API, and the subject of the ID token that came with it.</summary>
public sealed record BibliotaphGrant(string RefreshToken, string Subject);
