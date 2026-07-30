namespace Nornis.Web.State;

/// <summary>
/// Whether this circuit's API credentials still work, learned from the API's own answers.
///
/// <para>A Blazor Server circuit makes no full HTTP requests, so the cookie path that would
/// normally refresh or cleanly end an expired session never runs — and when the silent token
/// refresh fails (or the cookie predates offline_access and has no refresh token),
/// <c>BearerTokenHandler</c> falls back to the stale token. Every background poll then 401s,
/// forever, and nothing on screen says so: ~400 unauthorized requests over three hours from a
/// single badge poll, observed in production on 2026-07-27.</para>
///
/// <para>Set from <c>NornisApiClient</c> on any 401, cleared on any success. Clearing on success
/// is what makes a transient refresh failure — an Auth0 outage that has since recovered — heal
/// itself: the next attempt goes back through the refresher, and one working response ends the
/// expired state. Pollers consult <see cref="Expired"/> to stand down to a slow probe instead of
/// hammering, and the nav shows the state instead of letting the badge fail silently.</para>
/// </summary>
public sealed class AuthSessionState
{
    /// <summary>True after the API rejected a request as unauthenticated, until one succeeds.</summary>
    public bool Expired { get; private set; }

    public event Action? Changed;

    public void NotifyUnauthorized()
    {
        if (!Expired)
        {
            Expired = true;
            Changed?.Invoke();
        }
    }

    public void NotifyAuthorized()
    {
        if (Expired)
        {
            Expired = false;
            Changed?.Invoke();
        }
    }
}
