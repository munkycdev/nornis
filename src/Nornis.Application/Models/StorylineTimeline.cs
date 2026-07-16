namespace Nornis.Application.Models;

/// <summary>
/// Read model for the storyline timeline: every non-archived storyline as a lane of
/// dated developments, anchored to real-world session dates (Source.OccurredAt).
/// Sessions that touch multiple storylines are the convergence points; Links carry the
/// explicit storyline-to-storyline relationships.
/// </summary>
public record StorylineTimeline(
    IReadOnlyList<TimelineSession> Sessions,
    IReadOnlyList<TimelineLane> Lanes,
    IReadOnlyList<TimelineLink> Links);

public record TimelineSession(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    int StorylineCount);

public record TimelineLane(
    Guid StorylineId,
    string Name,
    string Status,
    IReadOnlyList<TimelinePoint> Points,
    Guid? ParentStorylineId,
    string? CampaignName);

/// <summary>One session's worth of developments on one storyline.</summary>
public record TimelinePoint(
    Guid SourceId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<TimelineDevelopment> Developments);

public record TimelineDevelopment(
    string Kind,
    string Text,
    string? Quote,
    bool IsOpenQuestion);

public record TimelineLink(
    Guid FromStorylineId,
    Guid ToStorylineId,
    string Type);
