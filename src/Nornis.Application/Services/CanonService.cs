using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class CanonService : ICanonService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;

    public CanonService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
    }

    public async Task<AppResult<IReadOnlyList<CanonEntry>>> GetCanonAsync(CanonQuery query, CancellationToken ct)
    {
        var allowedScopes = GetAllowedScopes(query.ActingUserRole);
        var isGm = query.ActingUserRole == CampaignRole.GM;

        // Only artifacts the caller may see contribute to canon. Facts and relationships have
        // no campaign FK, so campaign-wide retrieval goes through the visible artifact ids.
        var artifacts = await _artifactRepository.ListByCampaignAsync(query.CampaignId, null, null, ct);
        var artifactsById = artifacts
            .Where(a => allowedScopes.Contains(a.Visibility))
            .ToDictionary(a => a.Id);

        if (artifactsById.Count == 0)
        {
            return AppResult<IReadOnlyList<CanonEntry>>.Success([]);
        }

        var visibleIds = artifactsById.Keys.ToList();

        var facts = await _factRepository.ListByArtifactIdsAsync(visibleIds, int.MaxValue, ct);
        var relationships = await _relationshipRepository.ListByArtifactIdsAsync(visibleIds, allowedScopes, ct);

        var entries = new List<CanonEntry>();

        foreach (var fact in facts)
        {
            if (!allowedScopes.Contains(fact.Visibility))
                continue;
            if (!artifactsById.TryGetValue(fact.ArtifactId, out var artifact))
                continue;
            if (!IncludeTruthState(fact.TruthState, query.TruthState, isGm))
                continue;

            entries.Add(new CanonEntry(
                Kind: CanonEntryKind.Fact,
                Id: fact.Id,
                ArtifactId: artifact.Id,
                ArtifactName: artifact.Name,
                OtherArtifactId: null,
                OtherArtifactName: null,
                Label: fact.Predicate,
                Detail: fact.Value,
                Confidence: fact.Confidence,
                TruthState: fact.TruthState,
                Visibility: fact.Visibility,
                UpdatedAt: fact.UpdatedAt));
        }

        foreach (var relationship in relationships)
        {
            // Both endpoints must be visible so we never reveal the existence of a hidden
            // counterpart artifact through a canon relationship.
            if (!artifactsById.TryGetValue(relationship.ArtifactAId, out var artifactA)
                || !artifactsById.TryGetValue(relationship.ArtifactBId, out var artifactB))
                continue;
            if (!IncludeTruthState(relationship.TruthState, query.TruthState, isGm))
                continue;

            entries.Add(new CanonEntry(
                Kind: CanonEntryKind.Relationship,
                Id: relationship.Id,
                ArtifactId: artifactA.Id,
                ArtifactName: artifactA.Name,
                OtherArtifactId: artifactB.Id,
                OtherArtifactName: artifactB.Name,
                Label: relationship.Type,
                Detail: relationship.Description,
                Confidence: relationship.Confidence,
                TruthState: relationship.TruthState,
                Visibility: relationship.Visibility,
                UpdatedAt: relationship.UpdatedAt));
        }

        var ordered = entries
            .OrderByDescending(e => e.UpdatedAt)
            .ToList();

        return AppResult<IReadOnlyList<CanonEntry>>.Success(ordered);
    }

    /// <summary>
    /// Hidden truth-state entries reflect GM-only reality and are never surfaced to Players or
    /// Observers, regardless of the entry's visibility scope. An optional filter narrows the
    /// result to a single truth state.
    /// </summary>
    private static bool IncludeTruthState(TruthState truthState, TruthState? filter, bool isGm)
    {
        if (truthState == TruthState.Hidden && !isGm)
            return false;
        if (filter is not null && truthState != filter)
            return false;
        return true;
    }

    private static IReadOnlyList<VisibilityScope> GetAllowedScopes(CampaignRole role) =>
        role switch
        {
            CampaignRole.GM => [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private],
            CampaignRole.Player => [VisibilityScope.PartyVisible, VisibilityScope.Private],
            CampaignRole.Observer => [VisibilityScope.PartyVisible],
            _ => [VisibilityScope.PartyVisible]
        };
}
