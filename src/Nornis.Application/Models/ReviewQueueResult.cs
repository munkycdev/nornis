using Nornis.Domain.Entities;

namespace Nornis.Application.Models;

public record ReviewQueueResult(
    IReadOnlyList<ReviewProposal> Proposals,
    bool HasMore,
    IReadOnlyDictionary<Guid, ReviewProposalContext>? Context = null);

/// <summary>
/// Display context for a proposal, keyed by proposal id in <see cref="ReviewQueueResult.Context"/>:
/// which source produced it and a human-readable name for what it targets — so the review queue
/// never shows a reviewer a bare GUID.
/// </summary>
public record ReviewProposalContext(
    Guid SourceId,
    string SourceTitle,
    string? TargetName,
    string? MergeSourceName);
