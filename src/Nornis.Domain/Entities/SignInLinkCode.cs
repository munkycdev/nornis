namespace Nornis.Domain.Entities;

/// <summary>
/// The one outstanding code a user may have for linking another sign-in (feature 33). Keyed by
/// the user, so issuing a code replaces the previous one and the table never holds more rows
/// than there are users. Only the code's SHA-256 is kept.
/// </summary>
public class SignInLinkCode
{
    public Guid UserId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the code handed to the browser's sign-in round trip.</summary>
    public string CodeHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }
}
