using Nornis.Domain.Enums;

namespace Nornis.Application.Models;

/// <summary>
/// The session wrap-up: one GM-facing view assembled from data that already exists — what
/// recently advanced, what has gone quiet, what lineage the record suggests, and which recent
/// arcs still have no parent. Everything actionable here routes through the existing review /
/// status / parenting machinery; the wrap-up adds no parallel model.
/// </summary>
public record WrapUpView(
    bool HasWork,
    ContinuitySessionRef? LatestSession,
    IReadOnlyList<WrapUpAdvanced> Advanced,
    IReadOnlyList<QuietStoryline> GoneQuiet,
    IReadOnlyList<WrapUpNestSuggestion> CouldNest,
    IReadOnlyList<WrapUpUnparented> UnparentedArcs,
    IReadOnlyList<WrapUpParentOption> ParentOptions);

/// <summary>A storyline the last <c>k</c> sessions touched — read-only recap.</summary>
public record WrapUpAdvanced(
    Guid StorylineId,
    string Name,
    string Status,
    int RecentDevelopmentCount,
    DateTimeOffset LastDevelopmentAt);

/// <summary>
/// A pending <c>PartOf</c> lineage proposal (child → parent) surfaced from the review store,
/// acceptable/rejectable in place. Not recomputed here — this is an existing proposal.
/// </summary>
public record WrapUpNestSuggestion(
    Guid ProposalId,
    Guid ChildStorylineId,
    string ChildName,
    Guid ParentStorylineId,
    string ParentName,
    string? Rationale,
    decimal? Confidence);

/// <summary>A recent arc (first development within the window) that has no parent yet.</summary>
public record WrapUpUnparented(
    Guid StorylineId,
    string Name,
    string Status,
    DateTimeOffset FirstDevelopmentAt);

/// <summary>A storyline the GM may assign as a parent (populates the arc's parent picker).</summary>
public record WrapUpParentOption(
    Guid StorylineId,
    string Name,
    string Status);

// ------------------------------------------------------------------- Decisions --

/// <summary>
/// The GM's wrap-up decisions, applied in one call. Dismiss ("still active") is a client-side
/// snooze and carries no server decision, so it is absent here.
/// </summary>
public record WrapUpDecisionsCommand(
    Guid WorldId,
    Guid ActingUserId,
    WorldRole ActingUserRole,
    IReadOnlyList<WrapUpClosure> Closures,
    IReadOnlyList<Guid> AcceptProposalIds,
    IReadOnlyList<Guid> RejectProposalIds,
    IReadOnlyList<WrapUpParentAssignment> Parents);

/// <summary>Close a storyline. <see cref="Status"/> must be Dormant or Resolved.</summary>
public record WrapUpClosure(Guid StorylineId, ArtifactStatus Status);

public record WrapUpParentAssignment(Guid ChildStorylineId, Guid ParentStorylineId);

public record WrapUpResult(int Closed, int Nested, int Rejected, int Parented, Guid? BatchId);
