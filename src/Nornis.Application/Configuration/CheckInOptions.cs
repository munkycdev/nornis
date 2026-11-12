namespace Nornis.Application.Configuration;

/// <summary>
/// Tunables for the check-in (feature 44). Counted in dated sessions, like
/// <see cref="ContinuityOptions"/>: the world advances by play, not by the calendar. The number
/// of questions asked is the one knob that is per world (<c>World.CheckInQuestionCount</c>);
/// everything else is a server-wide default bound from the "CheckIn" section.
/// </summary>
public class CheckInOptions
{
    public const string SectionName = "CheckIn";

    /// <summary>Dated sessions after the last check-in's anchor before the next one is due.</summary>
    public int CadenceSessions { get; set; } = 3;

    /// <summary>Standing instructions a world may hold. The brief rides on every extraction call.</summary>
    public int MaxRules { get; set; } = 40;

    public int MaxStorylineCandidates { get; set; } = 15;

    public int MaxEntryCandidates { get; set; } = 10;

    public int MaxPatternCandidates { get; set; } = 5;

    /// <summary>A predicate is a pattern once this many facts carry it…</summary>
    public int MinPatternFacts { get; set; } = 8;

    /// <summary>…across at least this many entries.</summary>
    public int MinPatternArtifacts { get; set; } = 3;

    /// <summary>
    /// Check-ins whose answers keep a subject from being asked again. Two: a storyline the GM
    /// said is being pursued is not asked about next time, and is asked again the time after if
    /// it is still quiet.
    /// </summary>
    public int RecentCheckInsRemembered { get; set; } = 2;

    /// <summary>The choosing call's timeout. One call per check-in, with up to thirty candidates to weigh.</summary>
    public int AiTimeoutSeconds { get; set; } = 120;
}
