using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Shared read of every non-archived storyline and the dated developments attributed to it,
/// under one caller's visibility. This is the expensive, fiddly part of the storyline
/// timeline — reference attribution of facts, relationships, and artifact-level mentions to
/// the owning storyline and session. Both the timeline view and the continuity/staleness
/// signal project this same intermediate model, so the attribution logic lives in one place.
///
/// Stateless over its repositories; <see cref="ArtifactService"/> constructs it inline from
/// its own repos, while <see cref="StorylineContinuityService"/> takes it via DI.
/// </summary>
public sealed class StorylineDevelopmentReader
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly ISourceRepository _sourceRepository;

    public StorylineDevelopmentReader(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        ISourceRepository sourceRepository)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _sourceRepository = sourceRepository;
    }

    public static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false
    };

    public async Task<StorylineDevelopmentData> ReadAsync(
        Guid worldId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var filter = VisibilityFilter.ForRole(role, requestingUserId);

        // Every visible artifact — storylines become lanes; the rest resolve relationship
        // counterpart names. Archived storylines are merge leftovers and stay out.
        var allArtifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, null, ct))
            .Where(a => filter.CanSee(a.Visibility, a.CreatedByUserId))
            .ToDictionary(a => a.Id);

        var storylines = allArtifacts.Values
            .Where(a => a.Type == ArtifactType.Storyline && a.Status != ArtifactStatus.Archived)
            .ToList();

        if (storylines.Count == 0)
        {
            return StorylineDevelopmentData.Empty;
        }

        var storylineIds = storylines.Select(s => s.Id).ToList();
        var storylineIdSet = storylineIds.ToHashSet();

        // Hidden truth states are GM knowledge regardless of visibility scope — same gate
        // Ask and Canon apply, so neither the timeline nor staleness surfaces them to players.
        var facts = (await _factRepository.ListByArtifactIdsAsync(storylineIds, filter, int.MaxValue, ct))
            .Where(f => role == WorldRole.GM || f.TruthState != TruthState.Hidden)
            .ToList();

        var relationships = (await _relationshipRepository.ListByArtifactIdsAsync(storylineIds, filter, ct))
            .DistinctBy(r => r.Id)
            .ToList();

        // PartOf is structural — it becomes the lane's parent rather than a generic link.
        var parentByChild = relationships
            .Where(r => r.Type == ArtifactService.PartOfRelationshipType
                && storylineIdSet.Contains(r.ArtifactAId) && storylineIdSet.Contains(r.ArtifactBId))
            .GroupBy(r => r.ArtifactAId)
            .ToDictionary(g => g.Key, g => g.First().ArtifactBId);

        // Sessions the caller may see, dated. Undated sources (lore documents) carry no
        // position on a real-world axis and are skipped.
        var sources = (await _sourceRepository.ListByWorldAsync(worldId, null, ct))
            .Where(s => s.OccurredAt is not null && CanSeeSource(s, requestingUserId, role))
            .ToDictionary(s => s.Id);

        var targetIds = storylineIds
            .Concat(facts.Select(f => f.Id))
            .Concat(relationships.Select(r => r.Id))
            .ToList();
        var references = await _sourceReferenceRepository.ListByTargetIdsAsync(targetIds, ct);

        var factsById = facts.ToDictionary(f => f.Id);
        var relationshipsById = relationships.ToDictionary(r => r.Id);
        var quotesByTarget = references
            .GroupBy(r => (r.TargetId, r.SourceId))
            .ToDictionary(g => g.Key, g => g.First().Quote);

        // (storyline, source) → developments. A reference attributes its target to the
        // owning storyline: facts to their artifact, relationships to their storyline
        // endpoint(s), artifact-level citations to the storyline itself.
        var developments = new Dictionary<(Guid StorylineId, Guid SourceId), List<TimelineDevelopment>>();

        void Add(Guid storylineId, Guid sourceId, TimelineDevelopment development)
        {
            if (!sources.ContainsKey(sourceId))
            {
                return;
            }

            var key = (storylineId, sourceId);
            if (!developments.TryGetValue(key, out var list))
            {
                developments[key] = list = [];
            }
            if (!list.Any(d => d.Kind == development.Kind && d.Text == development.Text))
            {
                list.Add(development);
            }
        }

        foreach (var reference in references)
        {
            var quote = quotesByTarget.GetValueOrDefault((reference.TargetId, reference.SourceId));

            if (factsById.TryGetValue(reference.TargetId, out var fact))
            {
                var isOpenQuestion = string.Equals(fact.Predicate, "open question", StringComparison.OrdinalIgnoreCase)
                    && fact.TruthState != TruthState.False;
                // Open questions read as bare questions — the UI prefixes them, so the
                // predicate would just repeat itself.
                var text = isOpenQuestion ? fact.Value : $"{fact.Predicate}: {fact.Value}";
                Add(fact.ArtifactId, reference.SourceId,
                    new TimelineDevelopment("Fact", text, quote, isOpenQuestion));
            }
            else if (relationshipsById.TryGetValue(reference.TargetId, out var relationship))
            {
                foreach (var endpoint in new[] { relationship.ArtifactAId, relationship.ArtifactBId })
                {
                    if (!storylineIdSet.Contains(endpoint))
                    {
                        continue;
                    }

                    var otherId = endpoint == relationship.ArtifactAId ? relationship.ArtifactBId : relationship.ArtifactAId;
                    var otherName = allArtifacts.TryGetValue(otherId, out var other) ? other.Name : "another artifact";
                    Add(endpoint, reference.SourceId,
                        new TimelineDevelopment("Relationship", $"{relationship.Type} — {otherName}", quote, false));
                }
            }
            else if (storylineIdSet.Contains(reference.TargetId))
            {
                Add(reference.TargetId, reference.SourceId,
                    new TimelineDevelopment("Mention", "Storyline cited in this session", quote, false));
            }
        }

        var developmentsReadOnly = developments.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<TimelineDevelopment>)kv.Value);

        return new StorylineDevelopmentData(
            allArtifacts,
            storylines,
            facts,
            relationships,
            parentByChild,
            sources,
            developmentsReadOnly);
    }
}

/// <summary>
/// The intermediate model shared by the storyline timeline and the continuity signal.
/// <see cref="Developments"/> is keyed by (storyline, dated session); <see cref="Sources"/>
/// resolves a session id to its dated <see cref="Source"/>.
/// </summary>
public sealed record StorylineDevelopmentData(
    IReadOnlyDictionary<Guid, Artifact> AllArtifacts,
    IReadOnlyList<Artifact> Storylines,
    IReadOnlyList<ArtifactFact> Facts,
    IReadOnlyList<ArtifactRelationship> Relationships,
    IReadOnlyDictionary<Guid, Guid> ParentByChild,
    IReadOnlyDictionary<Guid, Source> Sources,
    IReadOnlyDictionary<(Guid StorylineId, Guid SourceId), IReadOnlyList<TimelineDevelopment>> Developments)
{
    public static readonly StorylineDevelopmentData Empty = new(
        new Dictionary<Guid, Artifact>(),
        [],
        [],
        [],
        new Dictionary<Guid, Guid>(),
        new Dictionary<Guid, Source>(),
        new Dictionary<(Guid, Guid), IReadOnlyList<TimelineDevelopment>>());
}
