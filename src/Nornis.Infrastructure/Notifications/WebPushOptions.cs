namespace Nornis.Infrastructure.Notifications;

/// <summary>
/// VAPID credentials identifying this application server to the push services.
///
/// The public key is handed to every browser at subscribe time and is not a secret. The private
/// key signs the push requests and is: it belongs in user secrets locally and in the container
/// app's secrets in Azure, never in appsettings.
///
/// Absent configuration is a supported state, not a failure. Notifications simply do not send,
/// and everything else works — a developer without keys should not be unable to run the app.
/// </summary>
public class WebPushOptions
{
    public const string SectionName = "WebPush";

    public string? PublicKey { get; set; }

    public string? PrivateKey { get; set; }

    /// <summary>
    /// Contact address for the push services, as "mailto:someone@example.com". Required by the
    /// VAPID spec so an operator can reach you if your sends misbehave.
    /// </summary>
    public string Subject { get; set; } = "mailto:support@nornis.app";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}
