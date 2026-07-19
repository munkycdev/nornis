namespace Nornis.Application.Models;

/// <summary>
/// Deterministic (no-AI) read of storyline continuity: which Active storylines have gone
/// quiet, measured in dated sessions since their last development. The signal that lets a
/// dropped arc stop hiding among the live ones.
/// </summary>
public record StorylineContinuityReport(
    int ActiveCount,
    int StaleThresholdSessions,
    ContinuitySessionRef? LatestSession,
    IReadOnlyList<QuietStoryline> Quiet,
    IReadOnlyList<UnanchoredStoryline> Unanchored);

/// <summary>A dated session, identified for the "since when" reference points.</summary>
public record ContinuitySessionRef(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt);

/// <summary>
/// An Active storyline with at least one dated development, but none in the last
/// <c>StaleThresholdSessions</c> sessions. Ordered most-quiet first.
/// </summary>
public record QuietStoryline(
    Guid StorylineId,
    string Name,
    string Status,
    DateTimeOffset? LastDevelopmentAt,
    int SessionsSinceLastDevelopment,
    int OpenQuestionCount,
    Guid? ParentStorylineId);

/// <summary>
/// An Active storyline the record cannot judge for staleness: created but never advanced by
/// any dated session, so there is no anchor to measure "sessions since" against.
/// </summary>
public record UnanchoredStoryline(
    Guid StorylineId,
    string Name,
    DateTimeOffset CreatedAt,
    int OpenQuestionCount);
