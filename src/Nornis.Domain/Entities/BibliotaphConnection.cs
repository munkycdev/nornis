using Nornis.Domain.Enums;

namespace Nornis.Domain.Entities;

/// <summary>
/// A user's grant to call Bibliotaph on their behalf: an Auth0 refresh token for the Bibliotaph
/// API, held by the API and the Worker (feature 31). One per user. A world uses its creator's
/// connection; co-GMs and players reach the creator's books through it.
///
/// Nothing about the Bibliotaph account is stored. The two apps are never linked by subject;
/// the grant itself is the link.
/// </summary>
public class BibliotaphConnection
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// The refresh token, as Data Protection ciphertext under the Bibliotaph ring, which only the
    /// API and Worker can read. Never logged and never in a response.
    /// </summary>
    public string ProtectedRefreshToken { get; set; } = string.Empty;

    public BibliotaphConnectionStatus Status { get; set; }

    public DateTimeOffset ConnectedAt { get; set; }

    public DateTimeOffset StatusChangedAt { get; set; }

    /// <summary>When Nornis last called Bibliotaph with it; written at most every five minutes. Null means never.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    public User User { get; set; } = null!;
}
