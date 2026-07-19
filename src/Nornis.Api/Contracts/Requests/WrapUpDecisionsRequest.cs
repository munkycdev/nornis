namespace Nornis.Api.Contracts.Requests;

/// <summary>
/// The GM's session-wrap-up decisions. All lists are optional; an empty request is a no-op.
/// Dismiss ("still active") is a client-side snooze and is not sent to the server.
/// </summary>
public record WrapUpDecisionsRequest(
    IReadOnlyList<WrapUpClosureRequest>? Closures,
    IReadOnlyList<Guid>? AcceptProposalIds,
    IReadOnlyList<Guid>? RejectProposalIds,
    IReadOnlyList<WrapUpParentAssignmentRequest>? Parents);

/// <summary><see cref="Status"/> must be "Dormant" or "Resolved".</summary>
public record WrapUpClosureRequest(Guid StorylineId, string Status);

public record WrapUpParentAssignmentRequest(Guid ChildStorylineId, Guid ParentStorylineId);
