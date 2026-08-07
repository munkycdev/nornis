using FsCheck;
using FsCheck.Fluent;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services.PropertyTests;

/// <summary>
/// Feature 21, Correctness Properties 2 and 3, quantified over generated worlds rather than
/// hand-picked ones — a candidate filter and a sort order are exactly the kind of rule that is
/// right for every example someone thought of and wrong for the shape they did not.
/// </summary>
[TestFixture]
[Category("Feature: convergence-gauge, Properties 2 and 3: candidate selection and determinism")]
public class ConvergenceCandidateSelectionTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: convergence-gauge, Property 2 — only hidden, non-private material is a candidate")]
    public Property OnlyHiddenNonPrivateMaterialIsACandidate()
    {
        return Prop.ForAll(WorldGen().ToArbitrary(), world =>
        {
            var gauge = ReadGauge(world);

            var factsById = world.Facts.ToDictionary(f => f.Id);
            var artifactsById = world.Artifacts.ToDictionary(a => a.Id);
            var relationshipsById = world.Relationships.ToDictionary(r => r.Id);

            return gauge.Candidates.All(candidate => candidate.Kind switch
            {
                ConvergenceCandidateKind.Fact =>
                    factsById.TryGetValue(candidate.Id, out var fact)
                    && fact.Visibility != VisibilityScope.Private
                    && (fact.Visibility == VisibilityScope.GMOnly || fact.TruthState == TruthState.Hidden),

                ConvergenceCandidateKind.Artifact =>
                    artifactsById.TryGetValue(candidate.Id, out var artifact)
                    && artifact.Visibility == VisibilityScope.GMOnly
                    && artifact.Status != ArtifactStatus.Archived,

                ConvergenceCandidateKind.Relationship =>
                    relationshipsById.TryGetValue(candidate.Id, out var relationship)
                    && relationship.Visibility == VisibilityScope.GMOnly,

                _ => false
            });
        });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: convergence-gauge, Property 3 — the same world state yields the same ranking")]
    public Property TheSameWorldStateYieldsTheSameRanking()
    {
        return Prop.ForAll(WorldGen().ToArbitrary(), world =>
        {
            var first = ReadGauge(world).Candidates.Select(c => c.Id).ToList();
            var second = ReadGauge(world).Candidates.Select(c => c.Id).ToList();

            return first.SequenceEqual(second);
        });
    }

    [FsCheck.NUnit.Property(MaxTest = 100)]
    [Description("Feature: convergence-gauge, Property 3 — the ranking is ordered by score, descending")]
    public Property TheRankingIsOrderedByScoreDescending()
    {
        return Prop.ForAll(WorldGen().ToArbitrary(), world =>
        {
            var scores = ReadGauge(world).Candidates.Select(c => c.Score).ToList();
            return scores.Zip(scores.Skip(1)).All(pair => pair.First >= pair.Second);
        });
    }

    #region Generated worlds

    private sealed record GeneratedWorld(
        IReadOnlyList<Artifact> Artifacts,
        IReadOnlyList<ArtifactFact> Facts,
        IReadOnlyList<ArtifactRelationship> Relationships);

    private static ConvergenceGauge ReadGauge(GeneratedWorld world)
    {
        var artifactRepo = new InMemoryArtifactRepository();
        var factRepo = new InMemoryArtifactFactRepository();
        var relationshipRepo = new InMemoryArtifactRelationshipRepository();

        artifactRepo.Seed(world.Artifacts);
        foreach (var fact in world.Facts)
        {
            factRepo.Seed(fact);
        }

        foreach (var relationship in world.Relationships)
        {
            relationshipRepo.Seed(relationship);
        }

        var service = new ConvergenceGaugeService(
            artifactRepo, factRepo, relationshipRepo, new InMemoryHealthAssessmentRepository());

        return service
            .GetGaugeAsync(WorldId, Guid.NewGuid(), WorldRole.GM, CancellationToken.None)
            .GetAwaiter().GetResult().Value!;
    }

    private static Gen<GeneratedWorld> WorldGen()
    {
        var visibility = Gen.Elements(
            VisibilityScope.Private, VisibilityScope.GMOnly, VisibilityScope.PartyVisible);
        var truth = Gen.Elements(
            TruthState.Confirmed, TruthState.Likely, TruthState.Rumor, TruthState.Hidden);
        var status = Gen.Elements(
            ArtifactStatus.Active, ArtifactStatus.Dormant, ArtifactStatus.Resolved, ArtifactStatus.Archived);
        var type = Gen.Elements(ArtifactType.Character, ArtifactType.Location, ArtifactType.Storyline);

        var artifactGen =
            from v in visibility
            from s in status
            from t in type
            from ageDays in Gen.Choose(0, 900)
            select new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = WorldId,
                Name = "entry",
                Type = t,
                Visibility = v,
                Status = s,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-ageDays),
                UpdatedAt = DateTimeOffset.UtcNow
            };

        return from artifacts in Gen.ListOf(artifactGen).Select(a => a.ToList())
               where artifacts.Count > 0
               from facts in Gen.ListOf(FactGen(artifacts, visibility, truth)).Select(f => f.ToList())
               from relationships in Gen.ListOf(RelationshipGen(artifacts, visibility)).Select(r => r.ToList())
               select new GeneratedWorld(artifacts, facts, relationships);
    }

    private static Gen<ArtifactFact> FactGen(
        List<Artifact> artifacts, Gen<VisibilityScope> visibility, Gen<TruthState> truth) =>
        from anchor in Gen.Elements<Artifact>(artifacts)
        from v in visibility
        from t in truth
        from ageDays in Gen.Choose(0, 900)
        select new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = anchor.Id,
            Predicate = "note",
            Value = "value",
            Visibility = v,
            TruthState = t,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-ageDays),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static Gen<ArtifactRelationship> RelationshipGen(
        List<Artifact> artifacts, Gen<VisibilityScope> visibility) =>
        from a in Gen.Elements<Artifact>(artifacts)
        from b in Gen.Elements<Artifact>(artifacts)
        from v in visibility
        from ageDays in Gen.Choose(0, 900)
        select new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = a.Id,
            ArtifactBId = b.Id,
            Type = "InvolvedIn",
            Visibility = v,
            TruthState = TruthState.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-ageDays),
            UpdatedAt = DateTimeOffset.UtcNow
        };

    #endregion
}
