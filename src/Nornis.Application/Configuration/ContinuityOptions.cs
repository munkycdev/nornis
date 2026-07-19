namespace Nornis.Application.Configuration;

/// <summary>
/// Tunables for the deterministic storyline-continuity signal and the session wrap-up it
/// feeds. Thresholds are counted in dated sessions, not days — the world advances by play,
/// not by the calendar. Per-world overrides are future work; this is a single world-wide
/// default bound from the "Continuity" configuration section.
/// </summary>
public class ContinuityOptions
{
    public const string SectionName = "Continuity";

    /// <summary>
    /// How many dated sessions may pass with no development on an Active storyline before it
    /// is flagged "quiet". A storyline touched in the latest session has zero sessions since.
    /// </summary>
    public int StaleThresholdSessions { get; set; } = 3;

    /// <summary>
    /// The "recent" window, in dated sessions, used by the wrap-up's Advanced, Could-nest,
    /// and Unparented-arcs sections.
    /// </summary>
    public int RecentSessionWindow { get; set; } = 2;
}
