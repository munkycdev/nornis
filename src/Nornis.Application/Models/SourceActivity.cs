namespace Nornis.Application.Models;

/// <summary>
/// The nav badge counts: what is in flight, what failed, and how much is waiting to be reviewed.
///
/// Deliberately a handful of integers computed by aggregate queries. This is the most frequently
/// requested thing in the system — polled from every open tab for the lifetime of the circuit —
/// so nothing here should require loading a row it does not count.
/// </summary>
/// <param name="PendingProposalsCapped">
/// True when the reviewer has more open proposals than the badge counts. The UI renders this as
/// "200+" rather than claiming an exact number it did not measure.
/// </param>
public record SourceActivity(
    int Ready,
    int Queued,
    int Processing,
    int Failed,
    int PendingProposals,
    bool PendingProposalsCapped);
