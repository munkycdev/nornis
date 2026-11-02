namespace Nornis.Domain.Entities;

/// <summary>
/// One way of signing in to a user's account: an Auth0 subject (<c>sub</c>), such as
/// <c>oauth2|discord|…</c> or <c>google-oauth2|…</c> (feature 33). A user has one or more, and a
/// subject belongs to at most one user. Nornis owns this map; accounts are never linked in Auth0,
/// where linking changes the subject every app on the shared tenant sees.
/// </summary>
public class UserSignIn
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The Auth0 subject, exactly as the token carries it.</summary>
    public string Subject { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
}
