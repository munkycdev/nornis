using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Enums;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class HealthService : IHealthService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly ISourceReferenceRepository _sourceReferenceRepository;

    private static readonly IReadOnlyList<VisibilityScope> AllScopes =
        [VisibilityScope.PartyVisible, VisibilityScope.GMOnly, VisibilityScope.Private];

    private const int RecencyWindowDays = 30;

    public HealthService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        ISourceReferenceRepository sourceReferenceRepository)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _sourceReferenceRepository = sourceReferenceRepository;
    }

    public async Task<AppResult<WorldHealth>> GetHealthAsync(Guid worldId, CancellationToken ct)
    {
        var artifacts = await _artifactRepository.ListByWorldAsync(worldId, null, null, ct);

        if (artifacts.Count == 0)
        {
            return AppResult<WorldHealth>.Success(
                new WorldHealth(HasData: false, 0, "Not enough data yet", 0, 0, 0, 0, 0, 0));
        }

        var artifactIds = artifacts.Select(a => a.Id).ToList();

        var facts = await _factRepository.ListByArtifactIdsAsync(artifactIds, int.MaxValue, ct);
        var relationships = await _relationshipRepository.ListByArtifactIdsAsync(artifactIds, AllScopes, ct);

        var statementCount = facts.Count + relationships.Count;

        // Which artifacts, facts, and relationships are cited by at least one source?
        var targetIds = new List<Guid>(artifactIds);
        targetIds.AddRange(facts.Select(f => f.Id));
        targetIds.AddRange(relationships.Select(r => r.Id));
        var sourceRefs = await _sourceReferenceRepository.ListByTargetIdsAsync(targetIds, ct);
        var sourcedIds = sourceRefs.Select(s => s.TargetId).ToHashSet();

        // Consistency — freedom from contradiction (Disputed / False lower it).
        var contradictions =
            facts.Count(f => f.TruthState is TruthState.Disputed or TruthState.False) +
            relationships.Count(r => r.TruthState is TruthState.Disputed or TruthState.False);
        var consistency = statementCount == 0 ? 100 : Percent(statementCount - contradictions, statementCount);

        // Completeness — artifacts fleshed out with a summary and at least one fact or relationship.
        var artifactsWithFacts = facts.Select(f => f.ArtifactId).ToHashSet();
        var artifactsWithRelationships = relationships
            .SelectMany(r => new[] { r.ArtifactAId, r.ArtifactBId })
            .ToHashSet();
        var developed = artifacts.Count(a =>
            !string.IsNullOrWhiteSpace(a.Summary) &&
            (artifactsWithFacts.Contains(a.Id) || artifactsWithRelationships.Contains(a.Id)));
        var completeness = Percent(developed, artifacts.Count);

        // Groundedness — statements traceable to a source.
        var grounded =
            facts.Count(f => sourcedIds.Contains(f.Id)) +
            relationships.Count(r => sourcedIds.Contains(r.Id));
        var groundedness = statementCount == 0 ? 0 : Percent(grounded, statementCount);

        // Recency — artifacts touched within the recency window.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-RecencyWindowDays);
        var recentlyUpdated = artifacts.Count(a => a.UpdatedAt >= cutoff);
        var recency = Percent(recentlyUpdated, artifacts.Count);

        var overall = (int)Math.Round((consistency + completeness + groundedness + recency) / 4.0);

        var health = new WorldHealth(
            HasData: true,
            OverallScore: overall,
            Label: LabelFor(overall),
            Consistency: consistency,
            Completeness: completeness,
            Groundedness: groundedness,
            Recency: recency,
            ArtifactCount: artifacts.Count,
            StatementCount: statementCount);

        return AppResult<WorldHealth>.Success(health);
    }

    private static int Percent(int numerator, int denominator) =>
        denominator == 0 ? 0 : (int)Math.Round(100.0 * numerator / denominator);

    private static string LabelFor(int score) => score switch
    {
        >= 85 => "Strong",
        >= 70 => "Healthy",
        >= 50 => "Fair",
        _ => "At risk",
    };
}
