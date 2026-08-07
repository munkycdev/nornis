using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

[TestFixture]
public class ConvergenceGaugeServiceTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemoryHealthAssessmentRepository _assessmentRepo = null!;
    private ConvergenceGaugeService _sut = null!;

    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmId = Guid.NewGuid();

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _assessmentRepo = new InMemoryHealthAssessmentRepository();

        _sut = new ConvergenceGaugeService(_artifactRepo, _factRepo, _relationshipRepo, _assessmentRepo);
    }

    private Task<Nornis.Application.Errors.AppResult<ConvergenceGauge>> Read(WorldRole role = WorldRole.GM) =>
        _sut.GetGaugeAsync(WorldId, GmId, role, CancellationToken.None);

    #region Authorization

    [Test]
    [Category("Authorization")]
    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task GetGauge_NonGm_Returns403(WorldRole role)
    {
        SeedArtifact("Captain Voss", VisibilityScope.GMOnly);

        var result = await Read(role);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    #endregion

    #region Candidate selection

    [Test]
    public async Task GetGauge_ExcludesPrivateMaterial()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "a private note", VisibilityScope.Private);
        // Private AND marked Hidden — the pairing the property test caught slipping through,
        // because the truth-state arm used to be an alternative to the visibility check rather
        // than subordinate to it.
        SeedFact(artifact, "a private hidden note", VisibilityScope.Private, TruthState.Hidden);
        SeedFact(artifact, "a genuine secret", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;

        // Private is the GM's workspace, not a secret with an audience waiting on it.
        Assert.That(gauge.Candidates.Select(c => c.Description), Has.None.Contains("a private note"));
        Assert.That(gauge.Candidates.Select(c => c.Description), Has.None.Contains("a private hidden note"));
        Assert.That(gauge.Candidates.Select(c => c.Description), Has.One.Contains("a genuine secret"));
    }

    [Test]
    public async Task GetGauge_ExcludesPartyVisibleMaterial()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "already known", VisibilityScope.PartyVisible);

        var gauge = (await Read()).Value!;

        Assert.That(gauge.Candidates, Is.Empty);
        Assert.That(gauge.TotalCandidates, Is.Zero);
    }

    [Test]
    public async Task GetGauge_TreatsAHiddenTruthStateAsACandidateEvenWhenPartyVisible()
    {
        // The party can see the shape of the claim but not its truth — still a reveal waiting.
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "true allegiance", VisibilityScope.PartyVisible, TruthState.Hidden);

        var gauge = (await Read()).Value!;

        Assert.That(gauge.Candidates, Has.Count.EqualTo(1));
        Assert.That(gauge.Candidates[0].Kind, Is.EqualTo(ConvergenceCandidateKind.Fact));
    }

    [Test]
    public async Task GetGauge_ExcludesArchivedArtifactsAndTheirFacts()
    {
        var archived = SeedArtifact("A merged duplicate", VisibilityScope.GMOnly, status: ArtifactStatus.Archived);
        SeedFact(archived, "a secret on dead weight", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;

        Assert.That(gauge.Candidates, Is.Empty, "a reveal cannot be pending on something removed from canon");
    }

    [Test]
    public async Task GetGauge_ReturnsAnEmptyGaugeForAWorldWithNothingHidden()
    {
        var gauge = (await Read()).Value!;

        // An empty gauge is a fact about the world, not an error.
        Assert.Multiple(() =>
        {
            Assert.That(gauge.Candidates, Is.Empty);
            Assert.That(gauge.WorldId, Is.EqualTo(WorldId));
            Assert.That(gauge.AssessmentId, Is.Null);
        });
    }

    #endregion

    #region Closure

    [Test]
    public async Task GetGauge_AFactOnAGmOnlyArtifact_ReportsThatArtifactAsTheClosure()
    {
        var artifact = SeedArtifact("The Vespergale cult", VisibilityScope.GMOnly);
        SeedFact(artifact, "its true patron", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;
        var fact = gauge.Candidates.Single(c => c.Kind == ConvergenceCandidateKind.Fact);

        Assert.Multiple(() =>
        {
            Assert.That(fact.MissingArtifactIds, Is.EquivalentTo([artifact.Id]));
            Assert.That(fact.Components.IsSelfContained, Is.False);
        });
    }

    [Test]
    public async Task GetGauge_AFactOnAPartyVisibleArtifact_IsSelfContained()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;
        var fact = gauge.Candidates.Single();

        Assert.Multiple(() =>
        {
            Assert.That(fact.MissingArtifactIds, Is.Empty);
            Assert.That(fact.Components.IsSelfContained, Is.True);
        });
    }

    [Test]
    public async Task GetGauge_ClosureAgreesWithTheRevealPrimitive()
    {
        // Correctness Property 4: the gauge and the reveal cannot disagree about what a reveal
        // costs, because the gauge does not compute it — RevealClosure does.
        var hiddenAnchor = SeedArtifact("The cult", VisibilityScope.GMOnly);
        var fact = SeedFact(hiddenAnchor, "its true patron", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;
        var candidate = gauge.Candidates.Single(c => c.Id == fact.Id);

        var direct = RevealClosure.MissingArtifactDependencies(
            revealArtifactIds: [],
            revealFactParentArtifactIds: [fact.ArtifactId],
            revealRelationshipEndpoints: [],
            new Dictionary<Guid, VisibilityScope> { [hiddenAnchor.Id] = hiddenAnchor.Visibility });

        Assert.That(candidate.MissingArtifactIds, Is.EquivalentTo(direct));
    }

    #endregion

    #region Components

    [Test]
    public async Task GetGauge_CountsOnlyPartyVisibleFactsAsFamiliarity()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "known one", VisibilityScope.PartyVisible);
        SeedFact(artifact, "known two", VisibilityScope.PartyVisible);
        SeedFact(artifact, "a private note", VisibilityScope.Private);
        var secret = SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;
        var candidate = gauge.Candidates.Single(c => c.Id == secret.Id);

        Assert.That(candidate.Components.PartyVisibleFactsOnAnchor, Is.EqualTo(2),
            "GM-only and Private facts are not knowledge the party has");
    }

    [Test]
    public async Task GetGauge_ReportsTheStorylineStatusTheAnchorTakesPartIn()
    {
        var storyline = SeedArtifact("The Missing Caravan", VisibilityScope.PartyVisible,
            type: ArtifactType.Storyline, status: ArtifactStatus.Resolved);
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedRelationship(voss, storyline, VisibilityScope.PartyVisible);
        var secret = SeedFact(voss, "true allegiance", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;
        var candidate = gauge.Candidates.Single(c => c.Id == secret.Id);

        Assert.That(candidate.Components.StorylineStatus, Is.EqualTo(ArtifactStatus.Resolved));
    }

    [Test]
    public async Task GetGauge_MeasuresDormancyFromCreation()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var secret = SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly,
            createdAt: DateTimeOffset.UtcNow.AddDays(-200));

        var gauge = (await Read()).Value!;
        var candidate = gauge.Candidates.Single(c => c.Id == secret.Id);

        Assert.Multiple(() =>
        {
            Assert.That(candidate.Components.DaysHidden, Is.EqualTo(200).Within(1));
            Assert.That(candidate.Components.Dormancy, Is.EqualTo(1.0).Within(0.0001));
        });
    }

    #endregion

    #region Contradictions

    [Test]
    public async Task GetGauge_WithNoAssessment_ReportsTheContradictionComponentUnavailable()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly);

        var gauge = (await Read()).Value!;
        var candidate = gauge.Candidates.Single();

        // "We did not look" and "we looked and found nothing" must not render identically.
        Assert.Multiple(() =>
        {
            Assert.That(gauge.AssessmentId, Is.Null);
            Assert.That(candidate.Components.ContradictionAssessed, Is.False);
            Assert.That(candidate.Components.ContradictionPressure, Is.Null);
        });
    }

    [Test]
    public async Task GetGauge_WithAnAssessmentCitingNothing_ReportsAMeasuredZero()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly);
        SeedAssessment();

        var gauge = (await Read()).Value!;
        var candidate = gauge.Candidates.Single();

        Assert.Multiple(() =>
        {
            Assert.That(gauge.AssessmentId, Is.Not.Null);
            Assert.That(candidate.Components.ContradictionAssessed, Is.True);
            Assert.That(candidate.Components.ContradictionPressure, Is.EqualTo(0.0));
        });
    }

    [Test]
    public async Task GetGauge_ACitedContradictionRaisesTheCandidateAboveAnUncitedTwin()
    {
        var contradicted = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        var quiet = SeedArtifact("Tavrin", VisibilityScope.PartyVisible);
        foreach (var anchor in new[] { contradicted, quiet })
        {
            for (var i = 0; i < ConvergenceWeights.FamiliaritySaturationFacts; i++)
            {
                SeedFact(anchor, $"known {i}", VisibilityScope.PartyVisible);
            }
        }

        var loud = SeedFact(contradicted, "true allegiance", VisibilityScope.GMOnly);
        var silent = SeedFact(quiet, "true allegiance", VisibilityScope.GMOnly);

        SeedAssessment(new ContinuityFinding
        {
            Id = Guid.NewGuid(),
            Category = ContinuityFindingCategory.Contradiction,
            Severity = ContinuityFindingSeverity.High,
            Status = ContinuityFindingStatus.Open,
            Summary = "The party believes Voss is loyal.",
            ArtifactId = contradicted.Id,
            EvidenceJson = "[]"
        });

        var gauge = (await Read()).Value!;

        Assert.That(gauge.Candidates[0].Id, Is.EqualTo(loud.Id), "the reveal with a deadline ranks first");
        Assert.That(gauge.Candidates.Single(c => c.Id == loud.Id).Score,
            Is.GreaterThan(gauge.Candidates.Single(c => c.Id == silent.Id).Score));
    }

    [Test]
    public async Task GetGauge_IgnoresFindingsThatAreNotOpenContradictions()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly);

        SeedAssessment(
            new ContinuityFinding
            {
                Id = Guid.NewGuid(),
                Category = ContinuityFindingCategory.Contradiction,
                Severity = ContinuityFindingSeverity.High,
                Status = ContinuityFindingStatus.Dismissed,
                Summary = "Dismissed.",
                ArtifactId = artifact.Id,
                EvidenceJson = "[]"
            },
            new ContinuityFinding
            {
                Id = Guid.NewGuid(),
                Category = ContinuityFindingCategory.DanglingThread,
                Severity = ContinuityFindingSeverity.High,
                Status = ContinuityFindingStatus.Open,
                Summary = "A different category.",
                ArtifactId = artifact.Id,
                EvidenceJson = "[]"
            });

        var gauge = (await Read()).Value!;

        Assert.That(gauge.Candidates.Single().Components.ContradictionSeverity, Is.Null);
    }

    #endregion

    #region Shape

    [Test]
    public async Task GetGauge_CapsTheListButReportsTheTrueTotal()
    {
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible);
        for (var i = 0; i < ConvergenceWeights.MaxCandidates + 12; i++)
        {
            SeedFact(artifact, $"secret {i}", VisibilityScope.GMOnly);
        }

        var gauge = (await Read()).Value!;

        // The gauge is a shortlist; a GM handed nine hundred rows has the flat list back.
        Assert.Multiple(() =>
        {
            Assert.That(gauge.Candidates, Has.Count.EqualTo(ConvergenceWeights.MaxCandidates));
            Assert.That(gauge.TotalCandidates, Is.EqualTo(ConvergenceWeights.MaxCandidates + 12));
        });
    }

    [Test]
    public async Task GetGauge_ChangesNothing()
    {
        // Correctness Property 1: the gauge is read-only.
        var artifact = SeedArtifact("Captain Voss", VisibilityScope.GMOnly);
        var fact = SeedFact(artifact, "true allegiance", VisibilityScope.GMOnly, TruthState.Hidden);

        await Read();

        Assert.Multiple(() =>
        {
            Assert.That(_artifactRepo.Artifacts.Single().Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(_factRepo.Facts.Single(f => f.Id == fact.Id).Visibility, Is.EqualTo(VisibilityScope.GMOnly));
            Assert.That(_factRepo.Facts.Single(f => f.Id == fact.Id).TruthState, Is.EqualTo(TruthState.Hidden));
        });
    }

    #endregion

    #region Seeding

    private Artifact SeedArtifact(
        string name,
        VisibilityScope visibility,
        ArtifactType type = ArtifactType.Character,
        ArtifactStatus status = ArtifactStatus.Active)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Name = name,
            Type = type,
            Visibility = visibility,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private ArtifactFact SeedFact(
        Artifact artifact,
        string value,
        VisibilityScope visibility,
        TruthState truthState = TruthState.Confirmed,
        DateTimeOffset? createdAt = null)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "note",
            Value = value,
            Visibility = visibility,
            TruthState = truthState,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _factRepo.Seed(fact);
        return fact;
    }

    private ArtifactRelationship SeedRelationship(Artifact a, Artifact b, VisibilityScope visibility)
    {
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = a.Id,
            ArtifactBId = b.Id,
            Type = "InvolvedIn",
            Visibility = visibility,
            TruthState = TruthState.Confirmed,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _relationshipRepo.Seed(relationship);
        return relationship;
    }

    private void SeedAssessment(params ContinuityFinding[] findings)
    {
        var assessment = new HealthAssessment
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Score = 70,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
        };
        foreach (var finding in findings)
        {
            finding.HealthAssessmentId = assessment.Id;
        }

        _assessmentRepo.CreateAsync(assessment, findings, CancellationToken.None).GetAwaiter().GetResult();
    }

    #endregion
}
