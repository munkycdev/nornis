using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

/// <summary>
/// Resolves a source's provenance rows (SourceReferences) into the artifacts, facts, and
/// relationships its extraction contributed, limited to what the reader may see. Rows whose
/// target no longer exists (knowledge later removed from canon) are silently skipped.
/// </summary>
public class SourceKnowledgeService : ISourceKnowledgeService
{
    private readonly ISourceRepository _sourceRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;

    public SourceKnowledgeService(
        ISourceRepository sourceRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository)
    {
        _sourceRepository = sourceRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
    }

    public async Task<AppResult<SourceKnowledge>> GetForSourceAsync(
        Guid worldId, Guid sourceId, Guid requestingUserId, WorldRole role, CancellationToken ct)
    {
        var source = await _sourceRepository.GetByIdAsync(sourceId, ct);
        if (source is null || source.WorldId != worldId || !SourceVisibilityRule.Compile(requestingUserId, role)(source))
        {
            return AppResult<SourceKnowledge>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        var references = await _sourceReferenceRepository.ListBySourceAsync(sourceId, ct);
        var filter = VisibilityFilter.ForRole(role, requestingUserId);

        var artifacts = new List<SourceKnowledgeArtifact>();
        var facts = new List<SourceKnowledgeFact>();
        var relationships = new List<SourceKnowledgeRelationship>();

        // An extraction batch for a full session routinely produces 50-200 accepted items, and
        // this ran a point lookup per one. Load each kind once up front instead, in two passes:
        // facts and relationships first, then every artifact anyone needs — the directly cited
        // ones plus each fact's owner and both ends of each relationship.
        var byType = references.ToLookup(r => r.TargetType);

        var factsById = (await _artifactFactRepository.ListByIdsAsync(
                byType[SourceReferenceTargetType.ArtifactFact].Select(r => r.TargetId).Distinct().ToList(), ct))
            .ToDictionary(f => f.Id);

        var relationshipsById = (await _artifactRelationshipRepository.ListByIdsAsync(
                byType[SourceReferenceTargetType.ArtifactRelationship].Select(r => r.TargetId).Distinct().ToList(), ct))
            .ToDictionary(r => r.Id);

        var neededArtifactIds = byType[SourceReferenceTargetType.Artifact].Select(r => r.TargetId)
            .Concat(factsById.Values.Select(f => f.ArtifactId))
            .Concat(relationshipsById.Values.Select(r => r.ArtifactAId))
            .Concat(relationshipsById.Values.Select(r => r.ArtifactBId))
            .Distinct()
            .ToList();

        var artifactsById = (await _artifactRepository.ListByIdsAsync(neededArtifactIds, ct))
            .ToDictionary(a => a.Id);

        // Set-based dedup replaces the linear scans over the accumulating result lists, which
        // were O(n²) across those same 50-200 items.
        var seenArtifacts = new HashSet<Guid>();
        var seenFacts = new HashSet<Guid>();
        var seenRelationships = new HashSet<Guid>();

        Artifact? ArtifactOrNull(Guid id) => artifactsById.GetValueOrDefault(id);

        // Still driven by reference order, so the Quote shown for an item remains the one from
        // its FIRST reference — the excerpts on this panel would otherwise silently change.
        foreach (var reference in references)
        {
            switch (reference.TargetType)
            {
                case SourceReferenceTargetType.Artifact:
                    {
                        var artifact = ArtifactOrNull(reference.TargetId);
                        if (artifact is not null && artifact.WorldId == worldId
                            && filter.CanSee(artifact.Visibility, artifact.CreatedByUserId)
                            && seenArtifacts.Add(artifact.Id))
                        {
                            artifacts.Add(new SourceKnowledgeArtifact(
                                artifact.Id, artifact.Name, artifact.Type.ToString(), reference.Quote));
                        }
                        break;
                    }
                case SourceReferenceTargetType.ArtifactFact:
                    {
                        if (!factsById.TryGetValue(reference.TargetId, out var fact)
                            || !filter.CanSee(fact.Visibility, fact.CreatedByUserId)
                            || seenFacts.Contains(fact.Id))
                        {
                            break;
                        }
                        var owner = ArtifactOrNull(fact.ArtifactId);
                        if (owner is not null && owner.WorldId == worldId)
                        {
                            seenFacts.Add(fact.Id);
                            facts.Add(new SourceKnowledgeFact(
                                fact.Id, owner.Id, owner.Name, fact.Predicate, fact.Value,
                                fact.TruthState.ToString(), fact.Visibility.ToString(), reference.Quote));
                        }
                        break;
                    }
                case SourceReferenceTargetType.ArtifactRelationship:
                    {
                        if (!relationshipsById.TryGetValue(reference.TargetId, out var relationship)
                            || relationship.WorldId != worldId
                            || !filter.CanSee(relationship.Visibility, relationship.CreatedByUserId)
                            || seenRelationships.Contains(relationship.Id))
                        {
                            break;
                        }
                        var a = ArtifactOrNull(relationship.ArtifactAId);
                        var b = ArtifactOrNull(relationship.ArtifactBId);
                        if (a is not null && b is not null)
                        {
                            seenRelationships.Add(relationship.Id);
                            relationships.Add(new SourceKnowledgeRelationship(
                                relationship.Id, a.Id, a.Name, relationship.Type, b.Id, b.Name, reference.Quote));
                        }
                        break;
                    }
            }
        }

        return AppResult<SourceKnowledge>.Success(new SourceKnowledge(artifacts, facts, relationships));
    }
}
