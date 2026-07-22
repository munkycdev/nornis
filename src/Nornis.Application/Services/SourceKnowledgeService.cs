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
        if (source is null || source.WorldId != worldId || !CanSeeSource(source, requestingUserId, role))
        {
            return AppResult<SourceKnowledge>.Fail(new AppError(404, "not_found", "Source not found."));
        }

        var references = await _sourceReferenceRepository.ListBySourceAsync(sourceId, ct);
        var filter = VisibilityFilter.ForRole(role, requestingUserId);

        var artifacts = new List<SourceKnowledgeArtifact>();
        var facts = new List<SourceKnowledgeFact>();
        var relationships = new List<SourceKnowledgeRelationship>();

        // Cache artifact lookups: facts and relationships resolve their owning artifacts too.
        var artifactCache = new Dictionary<Guid, Artifact?>();
        async Task<Artifact?> ArtifactAsync(Guid id)
        {
            if (!artifactCache.TryGetValue(id, out var artifact))
            {
                artifact = await _artifactRepository.GetByIdAsync(id, ct);
                artifactCache[id] = artifact;
            }
            return artifact;
        }

        foreach (var reference in references)
        {
            switch (reference.TargetType)
            {
                case SourceReferenceTargetType.Artifact:
                {
                    var artifact = await ArtifactAsync(reference.TargetId);
                    if (artifact is not null && artifact.WorldId == worldId
                        && Visible(filter, artifact.Visibility, artifact.CreatedByUserId)
                        && artifacts.All(a => a.ArtifactId != artifact.Id))
                    {
                        artifacts.Add(new SourceKnowledgeArtifact(
                            artifact.Id, artifact.Name, artifact.Type.ToString(), reference.Quote));
                    }
                    break;
                }
                case SourceReferenceTargetType.ArtifactFact:
                {
                    var fact = await _artifactFactRepository.GetByIdAsync(reference.TargetId, ct);
                    if (fact is null || !Visible(filter, fact.Visibility, fact.CreatedByUserId)
                        || facts.Any(f => f.FactId == fact.Id))
                    {
                        break;
                    }
                    var owner = await ArtifactAsync(fact.ArtifactId);
                    if (owner is not null && owner.WorldId == worldId)
                    {
                        facts.Add(new SourceKnowledgeFact(
                            fact.Id, owner.Id, owner.Name, fact.Predicate, fact.Value,
                            fact.TruthState.ToString(), fact.Visibility.ToString(), reference.Quote));
                    }
                    break;
                }
                case SourceReferenceTargetType.ArtifactRelationship:
                {
                    var relationship = await _artifactRelationshipRepository.GetByIdAsync(reference.TargetId, ct);
                    if (relationship is null || relationship.WorldId != worldId
                        || !Visible(filter, relationship.Visibility, relationship.CreatedByUserId)
                        || relationships.Any(r => r.RelationshipId == relationship.Id))
                    {
                        break;
                    }
                    var a = await ArtifactAsync(relationship.ArtifactAId);
                    var b = await ArtifactAsync(relationship.ArtifactBId);
                    if (a is not null && b is not null)
                    {
                        relationships.Add(new SourceKnowledgeRelationship(
                            relationship.Id, a.Id, a.Name, relationship.Type, b.Id, b.Name, reference.Quote));
                    }
                    break;
                }
            }
        }

        return AppResult<SourceKnowledge>.Success(new SourceKnowledge(artifacts, facts, relationships));
    }

    private static bool CanSeeSource(Source source, Guid userId, WorldRole role) => source.Visibility switch
    {
        VisibilityScope.PartyVisible => true,
        VisibilityScope.Private => role == WorldRole.GM || source.CreatedByUserId == userId,
        VisibilityScope.GMOnly => role == WorldRole.GM,
        _ => false,
    };

    // Mirrors VisibilityFilter semantics for a single row: scope must be readable, and
    // Private rows are additionally gated by ownership unless the reader is unrestricted.
    private static bool Visible(VisibilityFilter filter, VisibilityScope visibility, Guid? createdByUserId) =>
        filter.Scopes.Contains(visibility)
        && (visibility != VisibilityScope.Private
            || filter.PrivateOwnerUserId is null
            || createdByUserId == filter.PrivateOwnerUserId);
}
