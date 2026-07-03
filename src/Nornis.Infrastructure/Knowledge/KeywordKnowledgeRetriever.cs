using Microsoft.Extensions.Options;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Knowledge;

public class KeywordKnowledgeRetriever : IKnowledgeRetriever
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _artifactFactRepository;
    private readonly IArtifactRelationshipRepository _artifactRelationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;
    private readonly LoremasterOptions _options;

    public KeywordKnowledgeRetriever(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository artifactFactRepository,
        IArtifactRelationshipRepository artifactRelationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository,
        IOptions<LoremasterOptions> options)
    {
        _artifactRepository = artifactRepository;
        _artifactFactRepository = artifactFactRepository;
        _artifactRelationshipRepository = artifactRelationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
        _options = options.Value;
    }

    public async Task<KnowledgeContext> RetrieveAsync(
        string question,
        Guid campaignId,
        Guid userId,
        CampaignRole role,
        CancellationToken ct)
    {
        var allowedScopes = GetAllowedScopes(role);

        // 1. Name-matched artifacts
        var nameMatched = await _artifactRepository.ListByNamesInTextAsync(
            campaignId, question, allowedScopes, ct);

        // 2. Recent artifacts
        var recent = await _artifactRepository.ListRecentByCampaignAsync(
            campaignId, allowedScopes, _options.MaxRetrievalCount, ct);

        // 3. Merge and deduplicate (name-matched first, then recent), cap at MaxRetrievalCount
        var artifacts = MergeAndDeduplicate(nameMatched, recent, _options.MaxRetrievalCount);

        // Filter Private artifacts: only include those owned by the requesting user
        // Note: Artifact entity doesn't have a CreatedByUserId, so Private artifacts
        // returned by the repository are already filtered by visibility scope at query level.
        // The repository methods accept allowedVisibilities which handles visibility filtering.

        if (artifacts.Count == 0)
        {
            return new KnowledgeContext
            {
                Artifacts = [],
                Facts = [],
                Relationships = [],
                SourceReferences = []
            };
        }

        var artifactIds = artifacts.Select(a => a.Id).ToList();

        // 4. Load facts filtered by visibility
        var allFacts = await _artifactFactRepository.ListByArtifactIdsAsync(
            artifactIds, _options.MaxFactsPerArtifact, ct);

        var filteredFacts = allFacts
            .Where(f => IsVisibleToUser(f.Visibility, allowedScopes))
            .ToList();

        // 5. Load relationships filtered by visibility
        var relationships = await _artifactRelationshipRepository.ListByArtifactIdsAsync(
            artifactIds, allowedScopes, ct);

        // 6. Load source references for fact and relationship IDs
        var factIds = filteredFacts.Select(f => f.Id).ToList();
        var relationshipIds = relationships.Select(r => r.Id).ToList();
        var targetIds = factIds.Concat(relationshipIds).ToList();

        var sourceReferences = targetIds.Count > 0
            ? await _sourceReferenceRepository.ListByTargetIdsAsync(targetIds, ct)
            : [];

        // Map domain entities to Knowledge models
        return new KnowledgeContext
        {
            Artifacts = artifacts.Select(MapArtifact).ToList(),
            Facts = filteredFacts.Select(MapFact).ToList(),
            Relationships = relationships.Select(MapRelationship).ToList(),
            SourceReferences = sourceReferences.Select(MapSourceReference).ToList()
        };
    }

    internal static IReadOnlyList<VisibilityScope> GetAllowedScopes(CampaignRole role) =>
        role switch
        {
            CampaignRole.GM => [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private],
            CampaignRole.Player => [VisibilityScope.PartyVisible, VisibilityScope.Private],
            CampaignRole.Observer => [VisibilityScope.PartyVisible],
            _ => [VisibilityScope.PartyVisible]
        };

    private static bool IsVisibleToUser(VisibilityScope visibility, IReadOnlyList<VisibilityScope> allowedScopes) =>
        allowedScopes.Contains(visibility);

    private static IReadOnlyList<Artifact> MergeAndDeduplicate(
        IReadOnlyList<Artifact> nameMatched,
        IReadOnlyList<Artifact> recent,
        int maxCount)
    {
        var seen = new HashSet<Guid>();
        var result = new List<Artifact>();

        foreach (var artifact in nameMatched)
        {
            if (seen.Add(artifact.Id))
            {
                result.Add(artifact);
                if (result.Count >= maxCount)
                    return result;
            }
        }

        foreach (var artifact in recent)
        {
            if (seen.Add(artifact.Id))
            {
                result.Add(artifact);
                if (result.Count >= maxCount)
                    return result;
            }
        }

        return result;
    }

    private static KnowledgeArtifact MapArtifact(Artifact artifact) => new()
    {
        Id = artifact.Id,
        Name = artifact.Name,
        Type = artifact.Type.ToString(),
        Summary = artifact.Summary,
        ReferenceId = $"artifact:{artifact.Id}"
    };

    private static KnowledgeFact MapFact(ArtifactFact fact) => new()
    {
        Id = fact.Id,
        ArtifactId = fact.ArtifactId,
        Predicate = fact.Predicate,
        Value = fact.Value,
        TruthState = fact.TruthState,
        ReferenceId = $"fact:{fact.Id}"
    };

    private static KnowledgeRelationship MapRelationship(ArtifactRelationship relationship) => new()
    {
        Id = relationship.Id,
        ArtifactAId = relationship.ArtifactAId,
        ArtifactBId = relationship.ArtifactBId,
        Type = relationship.Type,
        Description = relationship.Description,
        TruthState = relationship.TruthState,
        ReferenceId = $"rel:{relationship.Id}"
    };

    private static KnowledgeSourceReference MapSourceReference(SourceReference sourceRef) => new()
    {
        Id = sourceRef.Id,
        SourceId = sourceRef.SourceId,
        TargetId = sourceRef.TargetId,
        Quote = sourceRef.Quote,
        ReferenceId = $"src:{sourceRef.Id}"
    };
}
