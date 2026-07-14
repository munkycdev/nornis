using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Application;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// GM-initiated merge uses the REAL ProposalApplicator over in-memory repositories, so
/// these tests verify actual merge semantics (facts move, duplicate archives) and the
/// provenance trail (synthetic source + accepted MergeArtifact proposal).
/// </summary>
[TestFixture]
public class ArtifactMergeServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid GmUserId = Guid.NewGuid();

    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private ArtifactMergeService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository();

        var applicator = new ProposalApplicator(
            _artifactRepo,
            _factRepo,
            _relationshipRepo,
            new InMemorySourceReferenceRepository(),
            _sourceRepo);

        _sut = new ArtifactMergeService(
            _artifactRepo,
            _sourceRepo,
            _batchRepo,
            _proposalRepo,
            applicator,
            new FakeUnitOfWork(),
            NullLogger<ArtifactMergeService>.Instance);
    }

    private Artifact SeedArtifact(string name, Guid? worldId = null)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = worldId ?? WorldId,
            Type = ArtifactType.Location,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(artifact);
        return artifact;
    }

    private void SeedFact(Guid artifactId, string predicate, string value) =>
        _factRepo.Seed(new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = predicate,
            Value = value,
            TruthState = TruthState.Confirmed,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

    [TestCase(WorldRole.Player)]
    [TestCase(WorldRole.Observer)]
    public async Task MergeAsync_NonGm_Returns403(WorldRole role)
    {
        var duplicate = SeedArtifact("Karvosthi");
        var target = SeedArtifact("Karvosti");

        var result = await _sut.MergeAsync(WorldId, duplicate.Id, target.Id, Guid.NewGuid(), role, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
        Assert.That(_artifactRepo.Artifacts.Single(a => a.Id == duplicate.Id).Status, Is.EqualTo(ArtifactStatus.Active));
    }

    [Test]
    public async Task MergeAsync_SelfMerge_Returns400()
    {
        var artifact = SeedArtifact("Karvosti");

        var result = await _sut.MergeAsync(WorldId, artifact.Id, artifact.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task MergeAsync_CrossWorldTarget_Returns404()
    {
        var duplicate = SeedArtifact("Karvosthi");
        var foreignTarget = SeedArtifact("Karvosti", worldId: Guid.NewGuid());

        var result = await _sut.MergeAsync(WorldId, duplicate.Id, foreignTarget.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task MergeAsync_MovesFactsArchivesDuplicateAndRecordsProvenance()
    {
        var duplicate = SeedArtifact("Karvosthi");
        var target = SeedArtifact("Karvosti");
        SeedFact(duplicate.Id, "location detail", "Plateau attack site");
        SeedFact(duplicate.Id, "open question", "Who attacked the plateau?");
        SeedFact(target.Id, "region", "Davokar");

        var result = await _sut.MergeAsync(WorldId, duplicate.Id, target.Id, GmUserId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(target.Id));

        // Facts moved
        Assert.That(_factRepo.Facts.Count(f => f.ArtifactId == target.Id), Is.EqualTo(3));
        Assert.That(_factRepo.Facts.Count(f => f.ArtifactId == duplicate.Id), Is.EqualTo(0));

        // Duplicate archived, target untouched
        Assert.That(_artifactRepo.Artifacts.Single(a => a.Id == duplicate.Id).Status, Is.EqualTo(ArtifactStatus.Archived));
        Assert.That(_artifactRepo.Artifacts.Single(a => a.Id == target.Id).Status, Is.EqualTo(ArtifactStatus.Active));

        // Provenance: synthetic source + accepted MergeArtifact proposal
        var source = _sourceRepo.Sources.Single();
        Assert.That(source.Title, Does.StartWith("Artifact merge — Karvosthi → Karvosti"));
        Assert.That(source.Visibility, Is.EqualTo(VisibilityScope.GMOnly));
        var proposal = _proposalRepo.Proposals.Single();
        Assert.That(proposal.ChangeType, Is.EqualTo(ReviewChangeType.MergeArtifact));
        Assert.That(proposal.Status, Is.EqualTo(ReviewProposalStatus.Accepted));
        Assert.That(proposal.ReviewedByUserId, Is.EqualTo(GmUserId));
        Assert.That(_batchRepo.Batches.Single().SourceId, Is.EqualTo(source.Id));
    }
}
