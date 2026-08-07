namespace Nornis.Domain.Enums;

public enum ContinuityFindingCategory
{
    Contradiction,
    DanglingThread,
    StaleStoryline,
    TimelineConflict,
    SummaryDrift,

    /// <summary>
    /// Two artifacts that are the same entity recorded twice. Unique among the categories in
    /// that its fix is destructive — accepting the drafted merge archives one artifact and
    /// moves its whole record onto the other — which is why the prompt's bar for proposing
    /// one is deliberately higher than for the advisory categories.
    /// </summary>
    DuplicateArtifact
}
