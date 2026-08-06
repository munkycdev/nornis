using Nornis.Application.Errors;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Application;

/// <summary>
/// Applies the proposed mutation to the knowledge graph.
/// </summary>
public interface IProposalApplicator
{
    /// <param name="source">
    /// The batch's source, already loaded by the caller. Every apply arm needs it — for the
    /// visibility it confers and for its author, who becomes the owner of anything created — and
    /// each used to re-read it by id, once per applied proposal. Both callers hold it before
    /// they get here, and a reveal constructs it in memory moments earlier, so re-reading was
    /// pure round trips inside an already-open transaction.
    /// </param>
    /// <param name="actingFilter">
    /// What the reviewer accepting this proposal may see. Payloads that reference an
    /// artifact by name resolve through this filter, so a proposal can never bind to an
    /// artifact its accepter cannot see. GM-gated callers pass
    /// <see cref="VisibilityFilter.All"/>.
    /// </param>
    Task<AppResult<ApplyResult>> ApplyAsync(
        ReviewProposal proposal,
        ReviewBatch batch,
        Source source,
        VisibilityFilter actingFilter,
        CancellationToken ct);
}

/// <param name="MatchedExistingArtifact">
/// True when a CreateArtifact was applied by binding to an artifact that already existed
/// instead of inserting one. Lets the review UI say "Matched existing 'X'" rather than
/// claiming it created something.
/// </param>
public record ApplyResult(
    Guid EntityId,
    SourceReferenceTargetType TargetType,
    bool MatchedExistingArtifact = false)
{
    /// <summary>
    /// Artifacts whose summary-relevant content this apply changed — the input to the
    /// accept-time summary refresh. Each arm reports what it actually touched, which is the
    /// one place that truth exists: for fact and relationship changes <see cref="EntityId"/>
    /// is the fact/relationship, not the owner; a merge stales its target; a PartOf move
    /// stales the child, the new parent, and the parent it left. Pure visibility and
    /// confidence changes report nothing — they alter who sees the record, not what a
    /// summary would say.
    /// </summary>
    public IReadOnlyList<Guid> SummaryRefreshCandidates { get; init; } = [];

    /// <summary>
    /// Artifacts whose summary this apply set explicitly. The reviewer chose that text, so
    /// the refresh must not immediately restate it — a pinned artifact is subtracted from
    /// the accept's whole refresh set, not just this proposal's.
    /// </summary>
    public IReadOnlyList<Guid> SummaryPinnedArtifactIds { get; init; } = [];
}
