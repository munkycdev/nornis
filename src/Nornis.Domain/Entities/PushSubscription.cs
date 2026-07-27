namespace Nornis.Domain.Entities;

/// <summary>
/// One browser that has agreed to receive notifications for one user. A person with a laptop
/// and a phone has two rows; clearing site data or reinstalling produces a new one rather than
/// updating the old, which is why <see cref="Endpoint"/> and not the user is the identity here.
///
/// Not world-scoped: permission is granted by a person to a browser, and the notification says
/// which world it came from. Scoping it per world would ask the same person for permission
/// again for every world they run.
/// </summary>
public class PushSubscription
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>
    /// The push service URL this browser gave us — Google's, Mozilla's, Apple's, whoever.
    /// Unique: the same endpoint arriving twice is the same browser re-subscribing, not a
    /// second device.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>Client public key for payload encryption (RFC 8291). Opaque to us.</summary>
    public string P256dh { get; set; } = string.Empty;

    /// <summary>Client auth secret for payload encryption. Opaque to us.</summary>
    public string Auth { get; set; } = string.Empty;

    /// <summary>Whatever the browser called itself when subscribing, so a user with several
    /// devices can tell which one they are turning off.</summary>
    public string? Label { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Last time a send to this endpoint succeeded. A subscription that has not been reachable
    /// for a long time is a browser that never came back; the push service will usually tell us
    /// outright with a 404 or 410, and we delete it then.
    /// </summary>
    public DateTimeOffset? LastSucceededAt { get; set; }

    // Navigation properties
    public User User { get; set; } = null!;
}
