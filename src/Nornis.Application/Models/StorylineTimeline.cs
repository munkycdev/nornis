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

/// <summary>
/// One storyline's arc.
///
/// A storyline is world-scoped and can thread through several campaigns, so
/// <paramref name="Campaigns"/> is the full set it spans — the ones a GM declared plus the
/// ones its dated sessions actually fall in — ordered by when each opened. The lane is not
/// bucketed into a single campaign by majority; it is shown spanning them all.
///
/// <paramref name="CampaignName"/>/<paramref name="CampaignStartedAt"/> name the lane's
/// <em>anchor</em> campaign only: the one whose band the row is drawn in, and by whose
/// declared start the timeline orders campaign bands. The anchor is the earliest-opening
/// campaign the lane spans (a GM declaration breaking a tie), never a vote. Both are null
/// when the lane touches no campaign.
/// </summary>
public record TimelineLane(
    Guid StorylineId,
    string Name,
    string Status,
    IReadOnlyList<TimelinePoint> Points,
    Guid? ParentStorylineId,
    IReadOnlyList<TimelineLaneCampaign> Campaigns,
    string? CampaignName,
    DateTimeOffset? CampaignStartedAt = null);

/// <summary>
/// A campaign a storyline lane spans. <paramref name="Declared"/> is a GM-curated membership;
/// <paramref name="Derived"/> means at least one of the lane's dated sessions falls in it. A
/// campaign can be either, or both — declared but not yet played, or played but never declared.
/// </summary>
public record TimelineLaneCampaign(
    Guid CampaignId,
    string Name,
    DateTimeOffset? StartedAt,
    bool Declared,
    bool Derived);

/// <summary>
/// One session's worth of developments on one storyline. <paramref name="CampaignId"/> is the
/// campaign that session belongs to, so the chart can mark where a lane crosses from one
/// campaign into another; null for sessions with no campaign.
/// </summary>
public record TimelinePoint(
    Guid SourceId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<TimelineDevelopment> Developments,
    Guid? CampaignId = null);

public record TimelineDevelopment(
    string Kind,
    string Text,
    string? Quote,
    bool IsOpenQuestion);

public record TimelineLink(
    Guid FromStorylineId,
    Guid ToStorylineId,
    string Type);
