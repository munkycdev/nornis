namespace Nornis.Application.Configuration;

/// <summary>
/// Tunables for the continuity-audit auto-trigger. The interval controls how often the hosted
/// service wakes; the two thresholds shape which campaigns are eligible on each tick. Defaults
/// give an hourly tick, a one-hour quiet period after the last acceptance, and at-most-daily runs.
/// </summary>
public class ContinuityAuditOptions
{
    /// <summary>How often the background trigger evaluates campaigns.</summary>
    public double TickIntervalHours { get; set; } = 1.0;

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
