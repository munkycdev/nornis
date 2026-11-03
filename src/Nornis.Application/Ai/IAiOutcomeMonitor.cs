using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>
/// Remembers how the last few AI calls to each provider went, so the status endpoint can report
/// on them without calling them.
///
/// An active probe would be a paid request on every scrape, which buys nothing a passive
/// reading of real traffic does not already give. The cost is scope: this is per-process
/// memory, so the API's reading covers API-side calls (Loremaster, audit, retrospectives,
/// embeddings) and says nothing about extraction, which runs in the worker. A dead
/// extraction path shows up as a stale worker heartbeat instead.
///
/// Since feature 39 each provider has its own history: a user's broken OpenAI-compatible endpoint
/// must not read as Nornis's Azure resource failing. Refusals (no key, a rejected key, an account
/// out of credit) are never recorded: they say nothing about the provider.
/// </summary>
public interface IAiOutcomeMonitor
{
    void Record(AiProvider provider, bool succeeded, DateTimeOffset at);

    /// <summary>The provider's outcomes within <paramref name="window"/> of <paramref name="now"/>.</summary>
    AiOutcomeSnapshot Snapshot(AiProvider provider, TimeSpan window, DateTimeOffset now);
}

/// <param name="Total">Calls observed in the window. Zero means idle, which is not a fault.</param>
/// <param name="Failures">How many of them failed.</param>
/// <param name="LastAt">When the most recent call landed, ignoring the window. Null if none ever has.</param>
public sealed record AiOutcomeSnapshot(int Total, int Failures, DateTimeOffset? LastAt);
