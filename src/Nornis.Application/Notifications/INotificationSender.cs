namespace Nornis.Application.Notifications;

/// <summary>
/// What a notification says. Deliberately small: a push payload has a hard size limit (about
/// 4KB after encryption, and some services are stricter), and anything long enough to need
/// scrolling belongs in the app rather than on a lock screen.
/// </summary>
/// <param name="Title">One line. Shown bold, truncated hard by the OS.</param>
/// <param name="Body">One or two lines of detail.</param>
/// <param name="Url">Where clicking it should land — a relative path within the app.</param>
/// <param name="Tag">Collapse key. Two notifications sharing a tag replace each other rather
/// than stacking, which is how a run that reports progress avoids becoming a wall.</param>
public record NotificationMessage(string Title, string Body, string Url, string? Tag = null);

/// <summary>
/// Delivers notifications to whatever browsers a person has enabled them on.
///
/// Every method is fire-and-forget from the caller's point of view: a notification that fails
/// to send must never fail the extraction, review, or import that prompted it. Implementations
/// swallow and log rather than throw.
/// </summary>
public interface INotificationSender
{
    /// <summary>Notifies one person on all of their subscribed browsers.</summary>
    Task NotifyUserAsync(Guid userId, NotificationMessage message, CancellationToken ct = default);

    /// <summary>Notifies several people at once — a world's GMs, say. Duplicate ids are fine.</summary>
    Task NotifyUsersAsync(IReadOnlyList<Guid> userIds, NotificationMessage message, CancellationToken ct = default);
}
