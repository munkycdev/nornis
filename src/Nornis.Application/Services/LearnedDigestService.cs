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

        var reveals = (await _sourceRepository.ListByWorldAsync(worldId, ct))
            .Where(s => s.Type == SourceType.Reveal)
            .Where(s => filter.CanSee(s.Visibility, s.CreatedByUserId))
            .Where(s => seenThrough is null || SortDate(s) > seenThrough.Value)
            .OrderByDescending(SortDate)
            .ThenByDescending(s => s.Id)
            .ToList();

        var limit = seenThrough is null ? FirstViewLimit : PageLimit;
        var hasMore = reveals.Count > limit;

        var entries = new List<LearnedEntry>();
        foreach (var reveal in reveals.Take(limit))
        {
            var elements = await ResolveAsync(reveal, filter, ct);

            // An entry whose elements have all been archived or hidden since is dropped rather
            // than rendered empty: "the GM revealed something and it is gone" is exactly the gap
            // that invites the question this view must not provoke.
            if (elements.Count > 0)
            {
                entries.Add(new LearnedEntry
                {
                    SourceId = reveal.Id,
                    OccurredAt = SortDate(reveal),
                    GmNote = reveal.RevealNote,
                    Elements = elements
                });
            }
        }

        return AppResult<LearnedDigest>.Success(new LearnedDigest
        {
            WorldId = worldId,
            GeneratedAt = now,
            SeenThrough = seenThrough,
            Entries = entries,
            HasMore = hasMore
        });
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
    /// A reveal source records when it happened in OccurredAt where one was set, and otherwise
    /// when it was written. Both orderings must agree with what the marker compares against, so
    /// the choice lives in one place.
    /// </summary>
    private static DateTimeOffset SortDate(Source source) => source.OccurredAt ?? source.CreatedAt;

    /// <summary>
    /// What a reveal promoted, resolved through the reader's own filter. Anything since
    /// archived, removed, or lowered simply does not come back, which is what makes "only
    /// party-visible material appears" structural rather than a rule to remember.
    /// </summary>
    private async Task<IReadOnlyList<LearnedElement>> ResolveAsync(
        Source reveal, VisibilityFilter filter, CancellationToken ct)
    {
        var batches = await _batchRepository.ListBySourceAsync(reveal.Id, ct);
        var revealBatch = batches.FirstOrDefault(b => b.Kind == ReviewBatchKinds.Reveal);
        if (revealBatch is null)
        {
            return [];
        }

        var proposals = (await _proposalRepository.ListByReviewBatchAsync(revealBatch.Id, ct))
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
                    elements.Add(new LearnedElement(artifact.Id, "Artifact", artifact.Name, artifact.Summary));
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
