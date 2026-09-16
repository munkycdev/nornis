using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public interface ILearnedDigestService
{
    /// <summary>
    /// The reveals this member has not yet seen, newest first. Read-only — reading is not
    /// seeing, so an interrupted reader does not lose the list.
    /// </summary>
    Task<AppResult<LearnedDigest>> GetAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);

    /// <summary>Advances this member's marker. Never backwards, never into the future.</summary>
    Task<AppResult<DateTimeOffset>> MarkSeenAsync(
        Guid worldId, Guid actingUserId, DateTimeOffset seenThrough, CancellationToken ct);

    /// <summary>
    /// How many disclosures this member has not seen — an aggregate for the nav badge, never
    /// the full read model. Counts what the reader may see and nothing else.
    /// </summary>
    Task<AppResult<int>> CountUnseenAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct);
}

public class LearnedDigestService : ILearnedDigestService
{
    /// <summary>
    /// What a member who has never looked is handed. Someone joining a world with two years of
    /// disclosures behind it needs the recent ones, not all of them.
    /// </summary>
    public const int FirstViewLimit = 10;

    /// <summary>The page's cap for a reader who has looked before.</summary>
    public const int PageLimit = 50;

    private readonly ISourceRepository _sourceRepository;
    private readonly IReviewBatchRepository _batchRepository;
    private readonly IReviewProposalRepository _proposalRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly IWorldMemberRepository _memberRepository;

    public LearnedDigestService(
        ISourceRepository sourceRepository,
        IReviewBatchRepository batchRepository,
        IReviewProposalRepository proposalRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        IWorldMemberRepository memberRepository)
    {
        _sourceRepository = sourceRepository;
        _batchRepository = batchRepository;
        _proposalRepository = proposalRepository;
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _memberRepository = memberRepository;
    }

    public async Task<AppResult<LearnedDigest>> GetAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var member = await _memberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        var seenThrough = member?.LearnedSeenAt;

        // The caller's own filter, not All: this is the gauge's opposite, and the party floor is
        // enforced by the same mechanism as everywhere else rather than by this remembering to.
        var filter = VisibilityFilter.ForRole(role, actingUserId);

        // Both kinds come from the same place: a source the reader may see, newer than their
        // marker, whose accepted proposals name what it put into the record. A reveal promotes
        // material that already existed; an extraction introduces new material. Chronological,
        // because the reader's question is "what happened since I looked", not "what kind".
        var candidates = (await _sourceRepository.ListLearnedCandidatesAsync(worldId, seenThrough, ct))
            .Where(c => filter.CanSee(c.Visibility, c.CreatedByUserId))
            .ToList();

        var limit = seenThrough is null ? FirstViewLimit : PageLimit;

        // One query for every candidate's batches. This read is also the nav badge's, and most
        // candidates settle right here at "no usable batch" — a note saved to the record without
        // extraction, a session whose every proposal was rejected.
        var batchesBySource = (await _batchRepository.ListBySourceIdsAsync(candidates.Select(c => c.Id).ToList(), ct))
            .ToLookup(b => b.SourceId);

        var entries = new List<LearnedEntry>();
        var considered = 0;

        foreach (var candidate in candidates)
        {
            if (entries.Count >= limit)
            {
                break;
            }

            considered++;

            // A reveal names its batch; an ordinary source's extraction batch is the one with no
            // kind at all — that null is what the filtered unique index keys on, and it is the
            // difference between "the GM told you" and "the record caught up".
            var batch = candidate.Type == SourceType.Reveal
                ? batchesBySource[candidate.Id].FirstOrDefault(b => b.Kind == ReviewBatchKinds.Reveal)
                : batchesBySource[candidate.Id].FirstOrDefault(b => b.Kind is null);

            if (batch is null)
            {
                continue;
            }

            var elements = await ResolveAsync(batch.Id, filter, ct);

            // An entry whose elements have all been archived or hidden since is dropped rather
            // than rendered empty: "the GM revealed something and it is gone" is exactly the gap
            // that invites the question this view must not provoke. The same rule retires a
            // session note whose every proposal was rejected — it recorded nothing.
            if (elements.Count == 0)
            {
                continue;
            }

            entries.Add(new LearnedEntry
            {
                Kind = candidate.Type == SourceType.Reveal
                    ? LearnedEntryKind.Disclosed
                    : LearnedEntryKind.Recorded,
                SourceId = candidate.Id,
                OccurredAt = candidate.OccurredAt,
                GmNote = candidate.Type == SourceType.Reveal ? candidate.RevealNote : null,
                Elements = elements
            });
        }

        var hasMore = candidates.Count > considered;

        return AppResult<LearnedDigest>.Success(new LearnedDigest
        {
            WorldId = worldId,
            GeneratedAt = now,
            SeenThrough = seenThrough,
            Entries = entries,
            HasMore = hasMore
        });
    }

    /// <summary>
    /// The page's own read, counted. The badge used to be a separate aggregate over reveal
    /// sources, and it disagreed with the page twice over: it never counted the session
    /// records the page shows, and it counted a reveal whose every element the page had since
    /// dropped — "2" on the nav above a page saying "nothing new". One read, one number: the
    /// badge is by construction the number of entries the reader will find.
    /// </summary>
    public async Task<AppResult<int>> CountUnseenAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        var digest = await GetAsync(worldId, actingUserId, role, ct);

        return digest.IsSuccess
            ? AppResult<int>.Success(digest.Value!.Entries.Count)
            : AppResult<int>.Fail(digest.Error!);
    }

    public async Task<AppResult<DateTimeOffset>> MarkSeenAsync(
        Guid worldId, Guid actingUserId, DateTimeOffset seenThrough, CancellationToken ct)
    {
        var member = await _memberRepository.GetByWorldAndUserAsync(worldId, actingUserId, ct);
        if (member is null)
        {
            return AppResult<DateTimeOffset>.Fail(new AppError(404, "not_found", "Membership not found."));
        }

        var advanced = LearnedMarker.Advance(member.LearnedSeenAt, seenThrough, DateTimeOffset.UtcNow);
        if (advanced != member.LearnedSeenAt)
        {
            member.LearnedSeenAt = advanced;
            await _memberRepository.UpdateAsync(member, ct);
        }

        return AppResult<DateTimeOffset>.Success(advanced);
    }

    /// <summary>
    /// What a batch put into the record, resolved through the reader's own filter. Anything
    /// since archived, removed, or lowered simply does not come back, which is what makes "only
    /// party-visible material appears" structural rather than a rule to remember.
    /// </summary>
    private async Task<IReadOnlyList<LearnedElement>> ResolveAsync(
        Guid batchId, VisibilityFilter filter, CancellationToken ct)
    {
        var proposals = (await _proposalRepository.ListByReviewBatchAsync(batchId, ct))
            .Where(p => p.Status == ReviewProposalStatus.Accepted && p.TargetId is not null)
            .ToList();

        var artifactIds = Targets(proposals, ReviewTargetType.Artifact);
        var factIds = Targets(proposals, ReviewTargetType.ArtifactFact);
        var relationshipIds = Targets(proposals, ReviewTargetType.ArtifactRelationship);

        var elements = new List<LearnedElement>();

        if (artifactIds.Count > 0)
        {
            foreach (var artifact in await _artifactRepository.ListByIdsAsync(artifactIds, ct))
            {
                if (artifact.Status != ArtifactStatus.Archived
                    && filter.CanSee(artifact.Visibility, artifact.CreatedByUserId))
                {
                    elements.Add(new LearnedElement(artifact.Id, "Artifact", artifact.Name, artifact.Summary, artifact.Slug));
                }
            }
        }

        if (factIds.Count > 0)
        {
            foreach (var fact in await _factRepository.ListByIdsAsync(factIds, ct))
            {
                // A fact promoted to party-visible but still marked Hidden is knowledge the party
                // can see the shape of, not the truth of — it has not been learned.
                if (fact.TruthState != TruthState.Hidden
                    && filter.CanSee(fact.Visibility, fact.CreatedByUserId))
                {
                    elements.Add(new LearnedElement(fact.Id, "Fact", fact.Predicate, fact.Value));
                }
            }
        }

        if (relationshipIds.Count > 0)
        {
            foreach (var relationship in await _relationshipRepository.ListByIdsAsync(relationshipIds, ct))
            {
                if (filter.CanSee(relationship.Visibility, relationship.CreatedByUserId))
                {
                    elements.Add(new LearnedElement(
                        relationship.Id, "Relationship", relationship.Type, relationship.Description));
                }
            }
        }

        return elements;
    }

    private static List<Guid> Targets(IReadOnlyList<ReviewProposal> proposals, ReviewTargetType type) =>
        proposals.Where(p => p.TargetType == type).Select(p => p.TargetId!.Value).Distinct().ToList();
}
