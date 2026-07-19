namespace Nornis.Api.Contracts.Responses;

public record WrapUpResponse(
    bool HasWork,
    ContinuitySessionRefResponse? LatestSession,
    IReadOnlyList<WrapUpAdvancedResponse> Advanced,
    IReadOnlyList<QuietStorylineResponse> GoneQuiet,
    IReadOnlyList<WrapUpNestSuggestionResponse> CouldNest,
    IReadOnlyList<WrapUpUnparentedResponse> UnparentedArcs,
    IReadOnlyList<WrapUpParentOptionResponse> ParentOptions);

public record WrapUpAdvancedResponse(
    Guid StorylineId,
    string Name,
    string Status,
    int RecentDevelopmentCount,
    DateTimeOffset LastDevelopmentAt);

public record WrapUpNestSuggestionResponse(
    Guid ProposalId,
    Guid ChildStorylineId,
    string ChildName,
    Guid ParentStorylineId,
    string ParentName,
    string? Rationale,
    decimal? Confidence);

public record WrapUpUnparentedResponse(
    Guid StorylineId,
    string Name,
    string Status,
    DateTimeOffset FirstDevelopmentAt);

public record WrapUpParentOptionResponse(
    Guid StorylineId,
    string Name,
    string Status);

public record WrapUpApplyResponse(
    int Closed,
    int Nested,
    int Rejected,
    int Parented,
    Guid? BatchId);
