namespace Nornis.Web.State;

/// <summary>
/// Raised when something happens that the nav's badges count: a proposal decided, a source
/// captured or deleted, a note sent for extraction.
///
/// The badges were previously driven by a 15-second poll alone, so accepting the last proposal
/// in the queue left "3" sitting in the sidebar until the next tick — long enough to read as
/// the app being wrong rather than late. Polling still runs as the backstop for changes this
/// browser did not make (a worker finishing an extraction); this is for the ones it did, where
/// waiting is indefensible because the answer is already known.
///
/// Scoped, like <see cref="WorldState"/>: one per circuit, so a signal never crosses users.
/// </summary>
public class ActivitySignal
{
    /// <summary>Fired after a change that the sidebar counts. Handlers must not throw.</summary>
    public event Action? Changed;

    /// <summary>
    /// Announce that the counts are stale. Safe to call from anywhere, including when nothing is
    /// listening — an unsubscribed signal is a no-op, not an error.
    /// </summary>
    public void Notify() => Changed?.Invoke();
}
