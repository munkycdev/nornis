using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Application;
using Nornis.Application.Errors;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Reveal runs over the REAL <see cref="ProposalApplicator"/> and in-memory repositories, so
/// these tests verify actual promotion semantics (visibility flips, party-visible provenance
/// stamped) and the closure guard — not just that the service calls a mock.
/// </summary>
[TestFixture]
public class RevealServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _referenceRepo = null!;
    private ProposalApplicator _applicator = null!;
    private RevealService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();
        _referenceRepo = new InMemorySourceReferenceRepository();

        _applicator = new ProposalApplicator(
            _artifactRepo, _factRepo, _relationshipRepo, _referenceRepo,
            _sourceRepo, new InMemorySourceAttachmentRepository(), new InMemoryMapPlacemarkRepository());

        _sut = BuildService(_applicator);
    }

    private RevealService BuildService(IProposalApplicator applicator) =>
        new(_artifactRepo, _factRepo, _relationshipRepo, _sourceRepo, _batchRepo, _proposalRepo,
            applicator, new FakeUnitOfWork(), NullLogger<RevealService>.Instance);

    // ---- authorization ----

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task RevealAsync_NonGm_Returns403_AndChangesNothing(WorldRole role)
    {
        var artifact = SeedArtifact("Black Harbor", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(role: role, artifacts: [artifact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(Current(artifact.Id).Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        Assert.That(_sourceRepo.Sources, Is.Empty);
    }

    // ---- happy paths, one per element kind ----

    [Test]
    public async Task RevealAsync_GmOnlyArtifact_BecomesPartyVisible_WithProvenance()
    {
        var artifact = SeedArtifact("Black Harbor", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(artifacts: [artifact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsClosed, Is.True);
        Assert.That(result.Value.RevealedArtifacts, Is.EqualTo(1));
        Assert.That(Current(artifact.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));

        // Provenance: a party-visible reveal source, a Reveal-kind completed batch, an accepted
        // UpdateArtifact proposal by the GM, and a party-visible reference onto the artifact.
        var source = _sourceRepo.Sources.Single();
        Assert.That(source.Type, Is.EqualTo(SourceType.Reveal));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(source.Title, Does.StartWith("Reveal —"));

        var batch = _batchRepo.Batches.Single();
        Assert.That(batch.Kind, Is.EqualTo("Reveal"));
        Assert.That(batch.Status, Is.EqualTo(ReviewBatchStatus.Completed));
        Assert.That(result.Value.BatchId, Is.EqualTo(batch.Id));

        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.UpdateArtifact));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(GmUserId));

        Assert.That(_referenceRepo.References.Any(r =>
                r.SourceId == source.Id
                && r.TargetType == SourceReferenceTargetType.Artifact
                && r.TargetId == artifact.Id),
            Is.True, "revealed artifact should carry a party-visible reveal reference");
    }

    [Test]
    public async Task RevealAsync_GmOnlyFact_OnVisibleArtifact_BecomesPartyVisible()
    {
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible, ArtifactType.Character);
        var secret = SeedFact(voss.Id, "true allegiance", "Smuggler king", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(facts: [secret.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.RevealedFacts, Is.EqualTo(1));
        Assert.That(CurrentFact(secret.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
    }

    [Test]
    public async Task RevealAsync_GmOnlyRelationship_BetweenVisibleArtifacts_BecomesPartyVisible()
    {
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible, ArtifactType.Character);
        var caravan = SeedArtifact("Missing Caravan", VisibilityScope.PartyVisible, ArtifactType.Storyline);
        var link = SeedRelationship(voss.Id, caravan.Id, "SuspectedIn", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(relationships: [link.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.RevealedRelationships, Is.EqualTo(1));
        Assert.That(CurrentRelationship(link.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
    }

    // ---- idempotence / no-op ----

    [Test]
    public async Task RevealAsync_AlreadyPartyVisible_IsNoOp_AndMintsNoBatch()
    {
        var artifact = SeedArtifact("Black Harbor", VisibilityScope.PartyVisible);

        var result = await _sut.RevealAsync(Command(artifacts: [artifact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.TotalRevealed, Is.EqualTo(0));
        Assert.That(result.Value.BatchId, Is.Null);
        Assert.That(_sourceRepo.Sources, Is.Empty);
        Assert.That(_batchRepo.Batches, Is.Empty);
    }

    // ---- closure ----

    [Test]
    public async Task RevealAsync_FactOnGmOnlyArtifact_NotClosed_ReportsMissingArtifact_AndAppliesNothing()
    {
        var harbor = SeedArtifact("Black Harbor", VisibilityScope.GMOnly);
        var fact = SeedFact(harbor.Id, "controls", "the smuggling docks", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(facts: [fact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsClosed, Is.False);
        Assert.That(result.Value.MissingArtifactIds, Is.EqualTo(new[] { harbor.Id }));
        // Nothing applied.
        Assert.That(CurrentFact(fact.Id).Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        Assert.That(_sourceRepo.Sources, Is.Empty);
    }

    [Test]
    public async Task RevealAsync_FactWithItsArtifactInSameSet_IsClosed_BothRevealed()
    {
        var harbor = SeedArtifact("Black Harbor", VisibilityScope.GMOnly);
        var fact = SeedFact(harbor.Id, "controls", "the smuggling docks", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(
            Command(artifacts: [harbor.Id], facts: [fact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.IsClosed, Is.True);
        Assert.That(result.Value.RevealedArtifacts, Is.EqualTo(1));
        Assert.That(result.Value.RevealedFacts, Is.EqualTo(1));
        Assert.That(Current(harbor.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(CurrentFact(fact.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
    }

    [Test]
    public async Task RevealAsync_RelationshipWithGmOnlyEndpoint_NotClosed_ReportsEndpoint()
    {
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible, ArtifactType.Character);
        var key = SeedArtifact("Silver Key", VisibilityScope.GMOnly, ArtifactType.Item);
        var link = SeedRelationship(voss.Id, key.Id, "Possesses", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(relationships: [link.Id]), CancellationToken.None);

        Assert.That(result.Value!.IsClosed, Is.False);
        Assert.That(result.Value.MissingArtifactIds, Is.EqualTo(new[] { key.Id }));
        Assert.That(CurrentRelationship(link.Id).Visibility, Is.EqualTo(VisibilityScope.GMOnly));
    }

    // ---- selective: reveal the place, not its secrets ----

    [Test]
    public async Task RevealAsync_RevealingArtifact_DoesNotRevealItsGmOnlyFacts()
    {
        var harbor = SeedArtifact("Black Harbor", VisibilityScope.GMOnly);
        var secret = SeedFact(harbor.Id, "hidden", "a smuggler's cache under the docks", VisibilityScope.GMOnly);

        var result = await _sut.RevealAsync(Command(artifacts: [harbor.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(Current(harbor.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
        Assert.That(CurrentFact(secret.Id).Visibility, Is.EqualTo(VisibilityScope.GMOnly),
            "a GM-only fact must not ride along when only its artifact is revealed");
    }

    // ---- corrections ----

    [Test]
    public async Task RevealAsync_Correction_ReTruthStatesFact_InTheSameBatch()
    {
        var voss = SeedArtifact("Captain Voss", VisibilityScope.PartyVisible, ArtifactType.Character);
        var belief = SeedFact(voss.Id, "reputation", "a trustworthy harbormaster",
            VisibilityScope.PartyVisible, TruthState.Confirmed);

        var result = await _sut.RevealAsync(
            Command(corrections: [new FactCorrection(belief.Id, TruthState.False)]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.Corrections, Is.EqualTo(1));
        var corrected = CurrentFact(belief.Id);
        Assert.That(corrected.TruthState, Is.EqualTo(TruthState.False));
        Assert.That(corrected.Visibility, Is.EqualTo(VisibilityScope.PartyVisible), "correction must not change visibility");
        Assert.That(_batchRepo.Batches.Single().Kind, Is.EqualTo("Reveal"));
    }

    // ---- validation ----

    [Test]
    public async Task RevealAsync_PrivateArtifact_Returns400()
    {
        var artifact = SeedArtifact("A player's private note subject", VisibilityScope.Private);

        var result = await _sut.RevealAsync(Command(artifacts: [artifact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("cannot_reveal_private"));
    }

    [Test]
    public async Task RevealAsync_ArtifactInAnotherWorld_Returns404()
    {
        var foreign = SeedArtifact("Black Harbor", VisibilityScope.GMOnly, worldId: Guid.NewGuid());

        var result = await _sut.RevealAsync(Command(artifacts: [foreign.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RevealAsync_UnknownFact_Returns404()
    {
        var result = await _sut.RevealAsync(Command(facts: [Guid.NewGuid()]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ---- rollback ----

    [Test]
    public async Task RevealAsync_WhenApplyFails_RollsBackAndSurfacesTheError()
    {
        var artifact = SeedArtifact("Black Harbor", VisibilityScope.GMOnly);
        var uow = new FakeUnitOfWork();
        var sut = new RevealService(
            _artifactRepo, _factRepo, _relationshipRepo, _sourceRepo, _batchRepo, _proposalRepo,
            new FailingApplicator(), uow, NullLogger<RevealService>.Instance);

        var result = await sut.RevealAsync(Command(artifacts: [artifact.Id]), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("boom"));
        Assert.That(uow.Transactions.Single().RolledBack, Is.True);
    }

    // ---- source reveal (phase 2) ----

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task RevealSourceAsync_NonGm_Returns403_AndChangesNothing(WorldRole role)
    {
        var source = SeedSource(VisibilityScope.GMOnly);

        var result = await _sut.RevealSourceAsync(WorldId, source.Id, GmUserId, role, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(CurrentSource(source.Id).Visibility, Is.EqualTo(VisibilityScope.GMOnly));
    }

    [Test]
    public async Task RevealSourceAsync_GmOnlySource_BecomesPartyVisible()
    {
        var source = SeedSource(VisibilityScope.GMOnly);

        var result = await _sut.RevealSourceAsync(WorldId, source.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WasAlreadyVisible, Is.False);
        Assert.That(CurrentSource(source.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
    }

    [Test]
    public async Task RevealSourceAsync_AlreadyPartyVisible_IsNoOp()
    {
        var source = SeedSource(VisibilityScope.PartyVisible);

        var result = await _sut.RevealSourceAsync(WorldId, source.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.WasAlreadyVisible, Is.True);
        Assert.That(CurrentSource(source.Id).Visibility, Is.EqualTo(VisibilityScope.PartyVisible));
    }

    [Test]
    public async Task RevealSourceAsync_PrivateSource_Returns400()
    {
        var source = SeedSource(VisibilityScope.Private);

        var result = await _sut.RevealSourceAsync(WorldId, source.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("cannot_reveal_private"));
        Assert.That(CurrentSource(source.Id).Visibility, Is.EqualTo(VisibilityScope.Private));
    }

    [Test]
    public async Task RevealSourceAsync_SourceInAnotherWorld_Returns404()
    {
        var foreign = SeedSource(VisibilityScope.GMOnly, worldId: Guid.NewGuid());

        var result = await _sut.RevealSourceAsync(WorldId, foreign.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RevealSourceAsync_UnknownSource_Returns404()
    {
        var result = await _sut.RevealSourceAsync(WorldId, Guid.NewGuid(), GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ---- helpers ----

    private RevealCommand Command(
        WorldRole role = WorldRole.GM,
        IEnumerable<Guid>? artifacts = null,
        IEnumerable<Guid>? facts = null,
        IEnumerable<Guid>? relationships = null,
        IEnumerable<FactCorrection>? corrections = null,
        string? note = null) =>
        new(WorldId, GmUserId, role,
            (artifacts ?? []).ToList(),
            (facts ?? []).ToList(),
            (relationships ?? []).ToList(),
            (corrections ?? []).ToList(),
            note);

    private Artifact SeedArtifact(
        string name, VisibilityScope visibility,
        ArtifactType type = ArtifactType.Location, Guid? worldId = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = type,
            Name = name,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private ArtifactFact SeedFact(
        Guid artifactId, string predicate, string value, VisibilityScope visibility,
        TruthState truthState = TruthState.Likely)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = truthState,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _factRepo.Seed(fact);
        return fact;
    }

    private ArtifactRelationship SeedRelationship(
        Guid aId, Guid bId, string type, VisibilityScope visibility)
    {
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = aId,
            ArtifactBId = bId,
            Type = type,
            TruthState = TruthState.Likely,
            Visibility = visibility,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _relationshipRepo.Seed(relationship);
        return relationship;
    }

    private Source SeedSource(VisibilityScope visibility, Guid? worldId = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = SourceType.Map,
            Title = "The GM's master map",
            Visibility = visibility,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GmUserId
        };
        _sourceRepo.Seed(source);
        return source;
    }

    private Artifact Current(Guid id) => _artifactRepo.Artifacts.Single(a => a.Id == id);
    private ArtifactFact CurrentFact(Guid id) => _factRepo.Facts.Single(f => f.Id == id);
    private ArtifactRelationship CurrentRelationship(Guid id) => _relationshipRepo.Relationships.Single(r => r.Id == id);
    private Source CurrentSource(Guid id) => _sourceRepo.Sources.Single(s => s.Id == id);

    private sealed class FailingApplicator : IProposalApplicator
    {
        public Task<AppResult<ApplyResult>> ApplyAsync(ReviewProposal proposal, ReviewBatch batch, CancellationToken ct) =>
            Task.FromResult(AppResult<ApplyResult>.Fail(new AppError(500, "boom", "Simulated apply failure.")));
    }
}
