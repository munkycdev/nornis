namespace Nornis.Api.Contracts.Responses;

/// <summary>
/// Lightweight world activity counts for navigation badges, respecting the caller's
/// visibility. PendingProposalsCapped means the count hit the review queue's page
/// limit and the true number is higher.
/// </summary>
public record SourceActivityResponse(
    int Ready,
    int Queued,
    int Processing,
    int Failed,
    int PendingProposals,
    bool PendingProposalsCapped);
