namespace Nornis.Application.Services;

/// <summary>
/// Where a member's "seen up to here" marker lands, given where it is and what they claim.
/// Pure, and separate from the service, because it is the whole of the marker's contract and
/// both of its rules are easier to trust as arithmetic than as a paragraph.
/// </summary>
public static class LearnedMarker
{
    /// <summary>
    /// Never backwards, and never into the future.
    ///
    /// Backwards matters because two tabs, or a client that posts a list it fetched ten minutes
    /// ago, would otherwise reopen reveals the reader has already closed. Forwards matters
    /// because a machine with a skewed clock could otherwise mark seen what has not happened,
    /// and there is no way back from that — the reader simply never sees those entries.
    /// </summary>
    public static DateTimeOffset Advance(DateTimeOffset? current, DateTimeOffset claimed, DateTimeOffset now)
    {
        var bounded = claimed > now ? now : claimed;
        return current is { } existing && existing > bounded ? existing : bounded;
    }
}
