namespace Nornis.Domain.Enums;

/// <summary>
/// Whether a user's grant to call Bibliotaph still works. <see cref="NeedsReconnect"/> means
/// Bibliotaph or Auth0 refused it: reads are skipped and the GM is asked to reconnect, while
/// world events are still queued and wait.
/// </summary>
public enum BibliotaphConnectionStatus
{
    Connected,
    NeedsReconnect,
}
