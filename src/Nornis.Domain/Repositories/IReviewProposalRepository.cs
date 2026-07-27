using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Domain.Repositories;

public interface IReviewProposalRepository
{
    Task<ReviewProposal> CreateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default);

    Task<ReviewProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReviewProposal>> ListByReviewBatchAsync(Guid reviewBatchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The world's OPEN proposals — Pending and Edited alike. An edit does not decide a
    /// proposal; it still needs an accept or a reject, and still holds its batch open.
    /// </summary>
    Task<IReadOnlyList<ReviewProposal>> ListPendingByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>Whether any proposal in the world has been decided (accepted or rejected)
    /// by a person. Tutorial detector for "vet the extraction".</summary>
    Task<bool> AnyDecidedByWorldAsync(Guid worldId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open (Pending or Edited) proposal counts across all of each source's batches, keyed
    /// by source id. Sources with nothing open are absent from the result. One round trip
    /// for a whole backlog — the import walk asks this for every item on every poll.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountOpenBySourcesAsync(
        IReadOnlyList<Guid> sourceIds,
        CancellationToken cancellationToken = default);

    Task<ReviewProposal> UpdateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default);

    /// <summary>
    /// The review queue page: open proposals (Pending or Edited) from the given sources,
    /// oldest batch first.
    /// </summary>
    /// <summary>
    /// How many open proposals this reviewer has waiting, counted in SQL, capped at
    /// <paramref name="limit"/> with a has-more flag — the same shape and the same scoping rules
    /// as <see cref="ListReviewQueueAsync"/>, but without materialising proposals, batches,
    /// sources or artifacts to arrive at one integer for a nav badge.
    ///
    /// Review scoping is deliberately STRICTER than source visibility: a Player may read a
    /// party-visible source they did not write, but may only review proposals on sources they
    /// authored. Observers review nothing.
    /// </summary>
    Task<(int Count, bool HasMore)> CountOpenForReviewerAsync(
        Guid worldId,
        Guid actingUserId,
        WorldRole actingUserRole,
        int limit,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)> ListReviewQueueAsync(
        Guid worldId,
        IReadOnlyList<Guid> allowedSourceIds,
        Guid? filterByBatchId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the <see cref="ReviewProposal.ReviewedAt"/> of the world's most recently
    /// accepted proposal, or null if the world has no accepted proposals. Drives the
    /// continuity-audit auto-trigger (a run is only warranted after new canon was accepted).
    /// </summary>
    Task<DateTimeOffset?> GetLatestAcceptanceTimeAsync(
        Guid worldId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the distinct world ids that have at least one accepted proposal — the only
    /// worlds the continuity-audit trigger needs to evaluate.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListWorldIdsWithAcceptancesAsync(
        CancellationToken cancellationToken = default);
}
