using System.Text.Json;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Application.Services;

public class ConvergenceGaugeService : IConvergenceGaugeService
{
    private readonly IArtifactRepository _artifactRepository;
    private readonly IArtifactFactRepository _factRepository;
    private readonly IArtifactRelationshipRepository _relationshipRepository;
    private readonly IHealthAssessmentRepository _assessmentRepository;

    public ConvergenceGaugeService(
        IArtifactRepository artifactRepository,
        IArtifactFactRepository factRepository,
        IArtifactRelationshipRepository relationshipRepository,
        IHealthAssessmentRepository assessmentRepository)
    {
        _artifactRepository = artifactRepository;
        _factRepository = factRepository;
        _relationshipRepository = relationshipRepository;
        _assessmentRepository = assessmentRepository;
    }

    public async Task<AppResult<ConvergenceGauge>> GetGaugeAsync(
        Guid worldId, Guid actingUserId, WorldRole role, CancellationToken ct)
    {
        if (role != WorldRole.GM)
        {
            return AppResult<ConvergenceGauge>.Fail(new AppError(403, "insufficient_role",
                "Only GMs can read the convergence gauge."));
        }

        var now = DateTimeOffset.UtcNow;

        // VisibilityFilter.All, deliberately: the gauge's entire subject is material below the
        // party floor, so a role-scoped read would return an empty gauge and look like a
        // working feature. The GM gate above is what makes that safe.
        var artifacts = (await _artifactRepository.ListByWorldAsync(worldId, null, ct))
            .Where(a => a.Status != ArtifactStatus.Archived)
            .ToList();

        if (artifacts.Count == 0)
        {
            return AppResult<ConvergenceGauge>.Success(EmptyGauge(worldId, now, assessmentId: null));
        }

        var artifactIds = artifacts.Select(a => a.Id).ToList();
        var artifactsById = artifacts.ToDictionary(a => a.Id);
        var visibilityByArtifact = artifacts.ToDictionary(a => a.Id, a => a.Visibility);

        var facts = await _factRepository.ListByArtifactIdsAsync(
            artifactIds, VisibilityFilter.All, ConvergenceWeights.MaxFactsPerArtifact, ct);
        var relationships = await _relationshipRepository.ListByArtifactIdsAsync(
            artifactIds, VisibilityFilter.All, ct);

        var assessment = await _assessmentRepository.GetLatestWithFindingsAsync(worldId, ct);
        var contradictions = BuildContradictionIndex(assessment);

        var partyVisibleFactCounts = facts
            .Where(f => f.Visibility == VisibilityScope.PartyVisible)
            .GroupBy(f => f.ArtifactId)
            .ToDictionary(g => g.Key, g => g.Count());

        var storylineStatusByArtifact = BuildStorylineStatusIndex(artifacts, artifactsById, relationships);

        var candidates = new List<ConvergenceCandidate>();

        foreach (var artifact in artifacts.Where(IsHiddenArtifact))
        {
            candidates.Add(BuildCandidate(
                ConvergenceCandidateKind.Artifact, artifact.Id, artifact.Id, artifact.Name,
                artifact.Name, artifact.CreatedAt,
                // Revealing an artifact adds no obligations of its own — see RevealClosure.
                missing: [],
                partyVisibleFactCounts, storylineStatusByArtifact, contradictions, assessment is not null, now));
        }

        foreach (var fact in facts.Where(IsHiddenFact))
        {
            if (!artifactsById.TryGetValue(fact.ArtifactId, out var anchor))
            {
                continue;
            }

            var missing = RevealClosure.MissingArtifactDependencies(
                revealArtifactIds: [],
                revealFactParentArtifactIds: [fact.ArtifactId],
                revealRelationshipEndpoints: [],
                visibilityByArtifact);

            candidates.Add(BuildCandidate(
                ConvergenceCandidateKind.Fact, fact.Id, anchor.Id, anchor.Name,
                $"{fact.Predicate}: {fact.Value}", fact.CreatedAt, missing,
                partyVisibleFactCounts, storylineStatusByArtifact, contradictions, assessment is not null, now));
        }

        foreach (var relationship in relationships.Where(r => r.Visibility == VisibilityScope.GMOnly))
        {
            if (!artifactsById.TryGetValue(relationship.ArtifactAId, out var anchor))
            {
                continue;
            }

            var missing = RevealClosure.MissingArtifactDependencies(
                revealArtifactIds: [],
                revealFactParentArtifactIds: [],
                revealRelationshipEndpoints: [(relationship.ArtifactAId, relationship.ArtifactBId)],
                visibilityByArtifact);

            var otherName = artifactsById.TryGetValue(relationship.ArtifactBId, out var other)
                ? other.Name
                : "an entry outside this world's active record";

            candidates.Add(BuildCandidate(
                ConvergenceCandidateKind.Relationship, relationship.Id, anchor.Id, anchor.Name,
                $"{relationship.Type} → {otherName}", relationship.CreatedAt, missing,
                partyVisibleFactCounts, storylineStatusByArtifact, contradictions, assessment is not null, now));
        }

        candidates.Sort(ConvergenceScore.Compare);

        return AppResult<ConvergenceGauge>.Success(new ConvergenceGauge
        {
            WorldId = worldId,
            GeneratedAt = now,
            AssessmentId = assessment?.Id,
            TotalCandidates = candidates.Count,
            Candidates = candidates.Count > ConvergenceWeights.MaxCandidates
                ? candidates.Take(ConvergenceWeights.MaxCandidates).ToList()
                : candidates
        });
    }

    // A hidden fact is one the party cannot see, or one they can see the shape of but not the
    // truth of. Private is excluded first and unconditionally: it is the GM's workspace, not a
    // secret with an audience waiting on it. Writing the truth-state arm as an alternative to
    // the GMOnly arm let a Private fact marked Hidden through — the property test found the
    // pairing, which the example tests had each covered singly and never together.
    private static bool IsHiddenFact(ArtifactFact fact) =>
        fact.Visibility != VisibilityScope.Private
        && (fact.Visibility == VisibilityScope.GMOnly || fact.TruthState == TruthState.Hidden);

    private static bool IsHiddenArtifact(Artifact artifact) =>
        artifact.Visibility == VisibilityScope.GMOnly;

    private static ConvergenceCandidate BuildCandidate(
        ConvergenceCandidateKind kind,
        Guid id,
        Guid anchorId,
        string anchorName,
        string description,
        DateTimeOffset createdAt,
        IReadOnlyList<Guid> missing,
        IReadOnlyDictionary<Guid, int> partyVisibleFactCounts,
        IReadOnlyDictionary<Guid, ArtifactStatus> storylineStatusByArtifact,
        IReadOnlyDictionary<Guid, ContinuityFindingSeverity> contradictions,
        bool assessed,
        DateTimeOffset now)
    {
        var daysHidden = Math.Max(0, (int)(now - createdAt).TotalDays);
        partyVisibleFactCounts.TryGetValue(anchorId, out var familiarFacts);
        storylineStatusByArtifact.TryGetValue(anchorId, out var storylineStatus);

        // The finding may cite the candidate itself or the artifact it hangs on; either means
        // the party currently believes something this contradicts.
        ContinuityFindingSeverity? severity = null;
        if (contradictions.TryGetValue(id, out var direct))
        {
            severity = direct;
        }
        else if (contradictions.TryGetValue(anchorId, out var viaAnchor))
        {
            severity = viaAnchor;
        }

        var components = ConvergenceScore.Components(
            daysHidden,
            familiarFacts,
            missing.Count,
            storylineStatusByArtifact.ContainsKey(anchorId) ? storylineStatus : null,
            severity,
            assessed);

        return new ConvergenceCandidate
        {
            Kind = kind,
            Id = id,
            AnchorArtifactId = anchorId,
            AnchorName = anchorName,
            Description = description,
            CreatedAt = createdAt,
            MissingArtifactIds = missing,
            Components = components,
            Score = ConvergenceScore.Total(components)
        };
    }

    /// <summary>
    /// Severity by cited id, from the latest assessment's open contradictions. Reuses Continuity
    /// Health's findings rather than defining contradiction a second time — a second definition
    /// would drift from the first within a release.
    /// </summary>
    private static Dictionary<Guid, ContinuityFindingSeverity> BuildContradictionIndex(
        HealthAssessment? assessment)
    {
        var index = new Dictionary<Guid, ContinuityFindingSeverity>();
        if (assessment is null)
        {
            return index;
        }

        foreach (var finding in assessment.Findings
                     .Where(f => f.Category == ContinuityFindingCategory.Contradiction
                                 && f.Status == ContinuityFindingStatus.Open))
        {
            if (finding.ArtifactId is { } artifactId)
            {
                Record(artifactId, finding.Severity);
            }

            foreach (var reference in DeserializeEvidence(finding.EvidenceJson))
            {
                if (Guid.TryParse(reference, out var referencedId))
                {
                    Record(referencedId, finding.Severity);
                }
            }
        }

        return index;

        // Keep the worst severity when several findings cite the same thing.
        void Record(Guid id, ContinuityFindingSeverity severity)
        {
            if (!index.TryGetValue(id, out var existing) || severity < existing)
            {
                index[id] = severity;
            }
        }
    }

    /// <summary>
    /// The most-finished storyline each artifact takes part in. A storyline is its own
    /// participant; everything else reaches one through a relationship of any type, since
    /// PartOf is storyline-to-storyline by construction.
    /// </summary>
    private static Dictionary<Guid, ArtifactStatus> BuildStorylineStatusIndex(
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyDictionary<Guid, Artifact> artifactsById,
        IReadOnlyList<ArtifactRelationship> relationships)
    {
        var index = new Dictionary<Guid, ArtifactStatus>();

        foreach (var storyline in artifacts.Where(a => a.Type == ArtifactType.Storyline))
        {
            Record(storyline.Id, storyline.Status);
        }

        foreach (var relationship in relationships)
        {
            TryLink(relationship.ArtifactAId, relationship.ArtifactBId);
            TryLink(relationship.ArtifactBId, relationship.ArtifactAId);
        }

        return index;

        void TryLink(Guid participantId, Guid maybeStorylineId)
        {
            if (artifactsById.TryGetValue(maybeStorylineId, out var candidate)
                && candidate.Type == ArtifactType.Storyline)
            {
                Record(participantId, candidate.Status);
            }
        }

        // Resolved outranks Dormant outranks Active — the enum's own order, descending.
        void Record(Guid artifactId, ArtifactStatus status)
        {
            if (!index.TryGetValue(artifactId, out var existing) || status > existing)
            {
                index[artifactId] = status;
            }
        }
    }

    private static IReadOnlyList<string> DeserializeEvidence(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static ConvergenceGauge EmptyGauge(Guid worldId, DateTimeOffset now, Guid? assessmentId) => new()
    {
        WorldId = worldId,
        GeneratedAt = now,
        AssessmentId = assessmentId,
        Candidates = [],
        TotalCandidates = 0
    };
}
