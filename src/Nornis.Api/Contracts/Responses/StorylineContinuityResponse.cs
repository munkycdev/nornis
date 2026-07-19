namespace Nornis.Api.Contracts.Responses;

public record StorylineContinuityResponse(
    int ActiveCount,
    int StaleThresholdSessions,
    ContinuitySessionRefResponse? LatestSession,
    IReadOnlyList<QuietStorylineResponse> Quiet,
    IReadOnlyList<UnanchoredStorylineResponse> Unanchored);

public record ContinuitySessionRefResponse(
    Guid SourceId,
    string Title,
    DateTimeOffset OccurredAt);

public record QuietStorylineResponse(
    Guid StorylineId,
    string Name,
    string Status,
    DateTimeOffset? LastDevelopmentAt,
    int SessionsSinceLastDevelopment,
    int OpenQuestionCount,
    Guid? ParentStorylineId);

public record UnanchoredStorylineResponse(
    Guid StorylineId,
    string Name,
    DateTimeOffset CreatedAt,
    int OpenQuestionCount);
