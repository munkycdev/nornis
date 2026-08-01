using Microsoft.EntityFrameworkCore;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Persistence.Repositories;

public class ReviewProposalRepository : IReviewProposalRepository
{
    private readonly NornisDbContext _context;

    public ReviewProposalRepository(NornisDbContext context)
    {
        _context = context;
    }

    public async Task<ReviewProposal> CreateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default)
    {
        _context.ReviewProposals.Add(proposal);
        await _context.SaveChangesAsync(cancellationToken);
        return proposal;
    }

    public async Task<ReviewProposal?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(rp => rp.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewProposal>> ListByReviewBatchAsync(Guid reviewBatchId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => rp.ReviewBatchId == reviewBatchId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewProposal>> ListPendingByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => _context.ReviewBatches.Any(rb => rb.Id == rp.ReviewBatchId && rb.WorldId == worldId))
            // Edited counts as open. An edited-but-undecided proposal still blocks batch
            // completion, so hiding it from the queue stranded the batch with nothing on
            // screen to act on.
            .Where(rp => rp.Status == ReviewProposalStatus.Pending
                || rp.Status == ReviewProposalStatus.Edited)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> AnyDecidedByWorldAsync(Guid worldId, CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => _context.ReviewBatches.Any(rb => rb.Id == rp.ReviewBatchId && rb.WorldId == worldId))
            .AnyAsync(rp => rp.Status != ReviewProposalStatus.Pending && rp.ReviewedByUserId != null, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountOpenBySourcesAsync(
        IReadOnlyList<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        if (sourceIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var counts = await _context.ReviewProposals
            .AsNoTracking()
            .Join(
                _context.ReviewBatches,
                rp => rp.ReviewBatchId,
                rb => rb.Id,
                (rp, rb) => new { Proposal = rp, Batch = rb })
            .Where(x => sourceIds.Contains(x.Batch.SourceId))
            // Same definition of "open" as the queue: an edit does not decide a proposal.
            .Where(x => x.Proposal.Status == ReviewProposalStatus.Pending
                || x.Proposal.Status == ReviewProposalStatus.Edited)
            .GroupBy(x => x.Batch.SourceId)
            .Select(g => new { SourceId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(c => c.SourceId, c => c.Count);
    }

    public async Task<ReviewProposal> UpdateAsync(ReviewProposal proposal, CancellationToken cancellationToken = default)
    {
        await _context.SaveAndDetachAsync(proposal, cancellationToken);
        return proposal;
    }

    public async Task<IReadOnlyList<ReviewProposal>> ListByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<(int Count, bool HasMore)> CountOpenForReviewerAsync(
        Guid worldId,
        Guid actingUserId,
        WorldRole actingUserRole,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Observers review nothing, so there is no query to run.
        if (actingUserRole is not (WorldRole.GM or WorldRole.Player))
        {
            return (0, false);
        }

        var restrictToOwnSources = actingUserRole == WorldRole.Player;

        var query = _context.ReviewProposals
            .AsNoTracking()
            .Join(
                _context.ReviewBatches,
                rp => rp.ReviewBatchId,
                rb => rb.Id,
                (rp, rb) => new { Proposal = rp, Batch = rb })
            .Where(x => x.Batch.WorldId == worldId)
            // Edited is open, same as Pending — mirrors ListReviewQueueAsync.
            .Where(x => x.Proposal.Status == ReviewProposalStatus.Pending
                || x.Proposal.Status == ReviewProposalStatus.Edited)
            // The reviewer scope, expressed as a join rather than a materialised id list.
            //
            // Both halves require the batch's source to still exist, matching the queue — which
            // derives its allowed-id list from the world's sources and so cannot include a batch
            // whose source is gone. Deleting a source removes its batches, so an orphan should be
            // impossible; if one ever occurred, the badge would otherwise show a number the
            // review page could not reproduce.
            .Where(x => _context.Sources.Any(s =>
                s.Id == x.Batch.SourceId
                && s.WorldId == worldId
                && (!restrictToOwnSources || s.CreatedByUserId == actingUserId)));

        // Counting limit + 1 reproduces the queue's has-more probe exactly, so the badge and the
        // queue agree on when the cap has been hit.
        var counted = await query.Take(limit + 1).CountAsync(cancellationToken);

        return counted > limit ? (limit, true) : (counted, false);
    }

    public async Task<(IReadOnlyList<ReviewProposal> Proposals, bool HasMore)> ListReviewQueueAsync(
        Guid worldId,
        IReadOnlyList<Guid> allowedSourceIds,
        Guid? filterByBatchId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var query = _context.ReviewProposals
            .AsNoTracking()
            .Join(
                _context.ReviewBatches,
                rp => rp.ReviewBatchId,
                rb => rb.Id,
                (rp, rb) => new { Proposal = rp, Batch = rb })
            .Where(x => x.Batch.WorldId == worldId)
            .Where(x => allowedSourceIds.Contains(x.Batch.SourceId))
            // Edited is open, same as Pending — see ListPendingByWorldAsync.
            .Where(x => x.Proposal.Status == ReviewProposalStatus.Pending
                || x.Proposal.Status == ReviewProposalStatus.Edited);

        if (filterByBatchId.HasValue)
        {
            query = query.Where(x => x.Proposal.ReviewBatchId == filterByBatchId.Value);
        }

        var results = await query
            .OrderBy(x => x.Batch.CreatedAt)
            .ThenBy(x => x.Proposal.CreatedAt)
            .Select(x => x.Proposal)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = results.Count > limit;
        var proposals = hasMore ? results.Take(limit).ToList() : results;

        return (proposals.AsReadOnly(), hasMore);
    }

    public async Task<DateTimeOffset?> GetLatestAcceptanceTimeAsync(
        Guid worldId, CancellationToken cancellationToken = default)
    {
        var latest = await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => rp.Status == ReviewProposalStatus.Accepted && rp.ReviewedAt != null)
            .Where(rp => _context.ReviewBatches.Any(rb => rb.Id == rp.ReviewBatchId && rb.WorldId == worldId))
            .OrderByDescending(rp => rp.ReviewedAt)
            .Select(rp => rp.ReviewedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return latest;
    }

    public async Task<IReadOnlyList<Guid>> ListWorldIdsWithAcceptancesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.ReviewProposals
            .AsNoTracking()
            .Where(rp => rp.Status == ReviewProposalStatus.Accepted)
            .Join(
                _context.ReviewBatches,
                rp => rp.ReviewBatchId,
                rb => rb.Id,
                (rp, rb) => rb.WorldId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }
}
