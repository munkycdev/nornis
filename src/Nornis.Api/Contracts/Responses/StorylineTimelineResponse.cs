namespace Nornis.Api.Contracts.Responses;

public record StorylineTimelineResponse(
    IReadOnlyList<TimelineSessionResponse> Sessions,
    IReadOnlyList<TimelineLaneResponse> Lanes,
    IReadOnlyList<TimelineLinkResponse> Links);

public record TimelineSessionResponse(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt,
    int StorylineCount);

public record TimelineLaneResponse(
    Guid StorylineId,
    string Name,
    string Status,
    IReadOnlyList<TimelinePointResponse> Points,
    Guid? ParentStorylineId,
    IReadOnlyList<TimelineLaneCampaignResponse> Campaigns,
    string? CampaignName,
    DateTimeOffset? CampaignStartedAt);

public record TimelineLaneCampaignResponse(
    Guid CampaignId,
    string Name,
    DateTimeOffset? StartedAt,
    bool Declared,
    bool Derived);

public record TimelinePointResponse(
    Guid SourceId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<TimelineDevelopmentResponse> Developments,
    Guid? CampaignId);

public record TimelineDevelopmentResponse(
    string Kind,
    string Text,
    string? Quote,
    bool IsOpenQuestion);

public record TimelineLinkResponse(
    Guid FromStorylineId,
    Guid ToStorylineId,
    string Type);
