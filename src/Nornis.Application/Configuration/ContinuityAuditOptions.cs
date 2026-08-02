namespace Nornis.Application.Configuration;

/// <summary>
/// Tunables for the continuity-audit auto-trigger. The interval controls how often the hosted
/// service wakes; the two thresholds shape which worlds are eligible on each tick. Defaults
/// give an hourly tick, a one-hour quiet period after the last acceptance, and at-most-daily runs.
/// </summary>
public class ContinuityAuditOptions
{
    public const string SectionName = "ContinuityAudit";

    /// <summary>
    /// How often the background trigger evaluates worlds. Zero or negative disables the trigger
    /// entirely — the same convention <see cref="AiBudgetOptions"/> uses — so that the natural
    /// reading of "0" is honoured instead of producing a delay-free loop.
    /// </summary>
    public double TickIntervalHours { get; set; } = 1.0;

    /// <summary>
    /// How long a world's audit claim stays valid before another host may take it over. This
    /// only matters when a host dies mid-assessment; on the happy path the claim is irrelevant
    /// because <see cref="MinIntervalHours"/> already gates the next run. Keep it well below
    /// <see cref="MinIntervalHours"/> so an interrupted run self-heals within the same day.
    /// </summary>
    public double ClaimTimeoutHours { get; set; } = 2.0;

    /// <summary>
    /// Minimum age of the latest accepted proposal before a run is warranted — a quiet period
    /// that lets a burst of related acceptances settle before spending an AI call.
    /// </summary>
    public double QuietPeriodHours { get; set; } = 1.0;

    /// <summary>
    /// Minimum time since the last assessment before another auto-run is allowed (at-most-daily).
    /// </summary>
    public double MinIntervalHours { get; set; } = 20.0;
}
