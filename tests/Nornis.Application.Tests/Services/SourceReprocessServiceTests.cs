using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Edit-and-reprocess cascade rules: knowledge solely attributable to the source is
/// deleted (facts/relationships it created; artifacts it created that nothing else
/// built on), while shared artifacts, entities it merely updated, and other sources'
/// contributions survive. The source's batches and references always go, and the
/// source is requeued for extraction.
/// </summary>
[TestFixture]
[Category("Feature: source-reprocess")]
public class SourceReprocessServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepository = null!;
    private InMemoryReviewBatchRepository _batchRepository = null!;
    private InMemoryReviewProposalRepository _proposalRepository = null!;
    private InMemorySourceReferenceRepository _referenceRepository = null!;
    private InMemoryArtifactRepository _artifactRepository = null!;
    private InMemoryArtifactFactRepository _factRepository = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepository = null!;
    private InMemoryCharacterRepository _characterRepository = null!;
    private FakeExtractionQueueClient _queueClient = null!;
    private FakeUnitOfWork _unitOfWork = null!;
    private SourceReprocessService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepository = new InMemorySourceRepository();
        _batchRepository = new InMemoryReviewBatchRepository();
        _proposalRepository = new InMemoryReviewProposalRepository(_batchRepository);
        _referenceRepository = new InMemorySourceReferenceRepository();
        _artifactRepository = new InMemoryArtifactRepository();
        _factRepository = new InMemoryArtifactFactRepository();
        _relationshipRepository = new InMemoryArtifactRelationshipRepository();
        _characterRepository = new InMemoryCharacterRepository();
        _queueClient = new FakeExtractionQueueClient();
        _unitOfWork = new FakeUnitOfWork();

        _sut = new SourceReprocessService(
            _sourceRepository,
            _batchRepository,
            _proposalRepository,
            _referenceRepository,
            _artifactRepository,
            _factRepository,
            _relationshipRepository,
            _characterRepository,
            _queueClient,
            _unitOfWork,
            NullLogger<SourceReprocessService>.Instance);
    }

    // ------------------------------------------------------------------ helpers --

    private Source SeedSource(
        SourceProcessingStatus status = SourceProcessingStatus.Processed,
        Guid? createdBy = null)
    {
        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.SessionNote,
            Title = "Session 12",
            Body = "We questioned Captain Voss in Black Harbor.",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = status,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = createdBy ?? OwnerId
        };
        _sourceRepository.Seed(source);
        return source;
    }

    private Artifact SeedArtifact(string name)
    {
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = name,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepository.Seed(artifact);
        return artifact;
    }

    private ArtifactFact SeedFact(Guid artifactId)
    {
        var fact = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifactId,
            Predicate = "location",
            Value = "Black Harbor",
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _factRepository.Seed(fact);
        return fact;
    }

    private ArtifactRelationship SeedRelationship(Guid artifactAId, Guid artifactBId)
    {
        var relationship = new ArtifactRelationship
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            ArtifactAId = artifactAId,
            ArtifactBId = artifactBId,
            Type = "LocatedIn",
            TruthState = TruthState.Likely,
            Visibility = VisibilityScope.PartyVisible,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _relationshipRepository.Seed(relationship);
        return relationship;
    }

    private ReviewBatch SeedBatch(Guid sourceId, ReviewBatchStatus status = ReviewBatchStatus.Completed)
    {
        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceId = sourceId,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _batchRepository.CreateAsync(batch).GetAwaiter().GetResult();
        return batch;
    }

    private void SeedAcceptedProposal(Guid batchId, ReviewChangeType changeType, Guid? targetId)
    {
        _proposalRepository.CreateAsync(new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batchId,
            ChangeType = changeType,
            TargetType = ReviewTargetType.Artifact,
            TargetId = targetId,
            ProposedValueJson = "{}",
            Rationale = "test",
            Status = ReviewProposalStatus.Accepted,
            CreatedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();
    }

    private void SeedReference(Guid sourceId, SourceReferenceTargetType targetType, Guid targetId)
    {
        _referenceRepository.Seed(new SourceReference
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            TargetType = targetType,
            TargetId = targetId,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static ReprocessSourceCommand Command(Guid sourceId, WorldRole role = WorldRole.GM,
        Guid? actingUserId = null, string? body = "Updated body.") =>
        new(sourceId, WorldId, actingUserId ?? OwnerId, role, Body: body);

    /// <summary>
    /// The full cascade scenario used by the preview and reprocess tests:
    /// - orphan: created by the source, nothing else touches it → deleted
    /// - shared: created by the source, but another source added a fact → kept
    /// - linked: created by the source, but a character links to it → kept
    /// - referenced: created by the source, another source references it → kept
    /// - foreign: created elsewhere; the source only updated a fact on it → untouched
    /// </summary>
    private (Source Source, Artifact Orphan, Artifact Shared, Artifact Linked, Artifact Referenced,
        ArtifactFact CreatedFact, ArtifactFact UpdatedFact, ArtifactFact ForeignFact,
        ArtifactRelationship CreatedRelationship) SeedCascadeScenario()
    {
        var source = SeedSource();
        var otherSourceId = Guid.NewGuid();
        var batch = SeedBatch(source.Id);

        var orphan = SeedArtifact("Captain Voss");
        var shared = SeedArtifact("Black Harbor");
        var linked = SeedArtifact("Tavrin");
        var referenced = SeedArtifact("Silver Key");
        var foreign = SeedArtifact("Missing Caravan");

        SeedAcceptedProposal(batch.Id, ReviewChangeType.CreateArtifact, orphan.Id);
        SeedAcceptedProposal(batch.Id, ReviewChangeType.CreateArtifact, shared.Id);
        SeedAcceptedProposal(batch.Id, ReviewChangeType.CreateArtifact, linked.Id);
        SeedAcceptedProposal(batch.Id, ReviewChangeType.CreateArtifact, referenced.Id);
        SeedReference(source.Id, SourceReferenceTargetType.Artifact, orphan.Id);
        SeedReference(source.Id, SourceReferenceTargetType.Artifact, shared.Id);
        SeedReference(source.Id, SourceReferenceTargetType.Artifact, linked.Id);
        SeedReference(source.Id, SourceReferenceTargetType.Artifact, referenced.Id);

        // Fact the source created on its own artifact → deleted with it.
        var createdFact = SeedFact(orphan.Id);
        SeedReference(source.Id, SourceReferenceTargetType.ArtifactFact, createdFact.Id);

        // Fact on the foreign artifact that the source only UPDATED → kept.
        var updatedFact = SeedFact(foreign.Id);
        SeedAcceptedProposal(batch.Id, ReviewChangeType.UpdateFact, updatedFact.Id);
        SeedReference(source.Id, SourceReferenceTargetType.ArtifactFact, updatedFact.Id);

        // Another source's fact on the shared artifact → keeps it alive.
        var foreignFact = SeedFact(shared.Id);
        SeedReference(otherSourceId, SourceReferenceTargetType.ArtifactFact, foreignFact.Id);

        // Relationship the source created between its own artifacts → deleted.
        var createdRelationship = SeedRelationship(orphan.Id, shared.Id);
        SeedAcceptedProposal(batch.Id, ReviewChangeType.AddRelationship, null);
        SeedReference(source.Id, SourceReferenceTargetType.ArtifactRelationship, createdRelationship.Id);

        // Another source's reference to the referenced artifact → keeps it alive.
        SeedReference(otherSourceId, SourceReferenceTargetType.Artifact, referenced.Id);

        // Character link keeps the linked artifact alive.
        _characterRepository.Seed(new Character
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            WorldMemberId = Guid.NewGuid(),
            Name = "Tavrin",
            ArtifactId = linked.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        // A pending proposal that will be discarded with the batch.
        _proposalRepository.CreateAsync(new ReviewProposal
        {
            Id = Guid.NewGuid(),
            ReviewBatchId = batch.Id,
            ChangeType = ReviewChangeType.AddFact,
            TargetType = ReviewTargetType.ArtifactFact,
            ProposedValueJson = "{}",
            Rationale = "pending",
            Status = ReviewProposalStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();

        return (source, orphan, shared, linked, referenced, createdFact, updatedFact, foreignFact, createdRelationship);
    }

    // ------------------------------------------------------------ authorization --

    [Test]
    public async Task Reprocess_Observer_Returns403()
    {
        var source = SeedSource();

        var result = await _sut.ReprocessAsync(Command(source.Id, WorldRole.Observer), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Reprocess_PlayerNotCreator_Returns403()
    {
        var source = SeedSource(createdBy: OtherUserId);

        var result = await _sut.ReprocessAsync(
            Command(source.Id, WorldRole.Player, actingUserId: OwnerId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(403));
    }

    [Test]
    public async Task Reprocess_GmOnAnotherUsersSource_Succeeds()
    {
        var source = SeedSource(createdBy: OtherUserId);
        SeedBatch(source.Id);

        var result = await _sut.ReprocessAsync(
            Command(source.Id, WorldRole.GM, actingUserId: OwnerId), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task Reprocess_WrongWorld_Returns404()
    {
        var source = SeedSource();
        var command = new ReprocessSourceCommand(source.Id, Guid.NewGuid(), OwnerId, WorldRole.GM, Body: "x");

        var result = await _sut.ReprocessAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [TestCase(SourceProcessingStatus.Draft)]
    [TestCase(SourceProcessingStatus.Ready)]
    [TestCase(SourceProcessingStatus.Queued)]
    [TestCase(SourceProcessingStatus.Processing)]
    public async Task Reprocess_NonReprocessableStatus_Returns409(SourceProcessingStatus status)
    {
        var source = SeedSource(status);

        var result = await _sut.ReprocessAsync(Command(source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task Reprocess_FailedSource_IsAllowed()
    {
        var source = SeedSource(SourceProcessingStatus.Failed);

        var result = await _sut.ReprocessAsync(Command(source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
    }

    [Test]
    public async Task Reprocess_InvalidTitle_Returns400AndDeletesNothing()
    {
        var scenario = SeedCascadeScenario();
        var command = new ReprocessSourceCommand(
            scenario.Source.Id, WorldId, OwnerId, WorldRole.GM, Title: "   ");

        var result = await _sut.ReprocessAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(_artifactRepository.Artifacts, Has.Count.EqualTo(5), "nothing is deleted on validation failure");
        Assert.That(_batchRepository.Batches, Is.Not.Empty);
    }

    // ---------------------------------------------------------------- preview --

    [Test]
    public async Task Preview_ReportsCascadeWithoutChangingAnything()
    {
        var scenario = SeedCascadeScenario();

        var result = await _sut.PreviewAsync(scenario.Source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var preview = result.Value!;
        Assert.That(preview.ArtifactNamesToDelete, Is.EquivalentTo(new[] { scenario.Orphan.Name }));
        Assert.That(preview.ArtifactNamesToKeep, Is.EquivalentTo(new[]
        {
            scenario.Shared.Name, scenario.Linked.Name, scenario.Referenced.Name
        }));
        Assert.That(preview.FactsToDelete, Is.EqualTo(1), "only the fact this source created");
        Assert.That(preview.RelationshipsToDelete, Is.EqualTo(1));
        Assert.That(preview.PendingProposalsToDiscard, Is.EqualTo(1));

        // Preview is read-only.
        Assert.That(_artifactRepository.Artifacts, Has.Count.EqualTo(5));
        Assert.That(_factRepository.Facts, Has.Count.EqualTo(3));
        Assert.That(_batchRepository.Batches, Is.Not.Empty);
        Assert.That(_queueClient.SentMessages, Is.Empty);
    }

    // ---------------------------------------------------------------- cascade --

    [Test]
    public async Task Reprocess_DeletesOnlySolelyAttributableKnowledge()
    {
        var scenario = SeedCascadeScenario();

        var result = await _sut.ReprocessAsync(Command(scenario.Source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);

        var remainingArtifactIds = _artifactRepository.Artifacts.Select(a => a.Id).ToList();
        Assert.That(remainingArtifactIds, Does.Not.Contain(scenario.Orphan.Id), "orphaned created artifact is deleted");
        Assert.That(remainingArtifactIds, Does.Contain(scenario.Shared.Id), "artifact with another source's fact survives");
        Assert.That(remainingArtifactIds, Does.Contain(scenario.Linked.Id), "character-linked artifact survives");
        Assert.That(remainingArtifactIds, Does.Contain(scenario.Referenced.Id), "artifact referenced by another source survives");

        var remainingFactIds = _factRepository.Facts.Select(f => f.Id).ToList();
        Assert.That(remainingFactIds, Does.Not.Contain(scenario.CreatedFact.Id), "created fact is deleted");
        Assert.That(remainingFactIds, Does.Contain(scenario.UpdatedFact.Id), "merely-updated fact survives");
        Assert.That(remainingFactIds, Does.Contain(scenario.ForeignFact.Id), "another source's fact survives");

        Assert.That(_relationshipRepository.Relationships.Select(r => r.Id),
            Does.Not.Contain(scenario.CreatedRelationship.Id), "created relationship is deleted");

        Assert.That(_referenceRepository.References.Where(r => r.SourceId == scenario.Source.Id), Is.Empty,
            "the source's provenance trail is cleared");
        Assert.That(_batchRepository.Batches, Is.Empty, "batches are deleted so extraction can run again");
    }

    [Test]
    public async Task Reprocess_AppliesEditsAndRequeues()
    {
        var scenario = SeedCascadeScenario();
        var command = new ReprocessSourceCommand(
            scenario.Source.Id, WorldId, OwnerId, WorldRole.GM,
            Title: "Session 12 (corrected)", Body: "It was actually Lieutenant Voss.");

        var result = await _sut.ReprocessAsync(command, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var source = result.Value!;
        Assert.That(source.Title, Is.EqualTo("Session 12 (corrected)"));
        Assert.That(source.Body, Is.EqualTo("It was actually Lieutenant Voss."));
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));

        Assert.That(_queueClient.SentMessages, Has.Count.EqualTo(1));
        Assert.That(_queueClient.SentMessages[0].SourceId, Is.EqualTo(scenario.Source.Id));
        Assert.That(_queueClient.SentMessages[0].WorldId, Is.EqualTo(WorldId));

        Assert.That(_unitOfWork.Transactions, Has.Count.EqualTo(1));
        Assert.That(_unitOfWork.Transactions[0].Committed, Is.True);
    }

    [Test]
    public async Task Reprocess_EnqueueFailure_LeavesSourceFailedWithRetryPath()
    {
        var scenario = SeedCascadeScenario();
        _queueClient.ConfigureToFail();

        var result = await _sut.ReprocessAsync(Command(scenario.Source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(502));

        // The cascade is committed; the source lands on Failed, which has the existing
        // mark-ready retry path (Failed → Ready → Queued).
        var source = (await _sourceRepository.GetByIdAsync(scenario.Source.Id, CancellationToken.None))!;
        Assert.That(source.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Failed));
        Assert.That(_batchRepository.Batches, Is.Empty, "cascade committed before the enqueue attempt");
    }

    [Test]
    public async Task Reprocess_CommitFailure_Returns500AndRollsBack()
    {
        var scenario = SeedCascadeScenario();
        _unitOfWork.ConfigureCommitFailure();

        var result = await _sut.ReprocessAsync(Command(scenario.Source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(500));
        Assert.That(_unitOfWork.Transactions[0].RolledBack, Is.True);
        Assert.That(_queueClient.SentMessages, Is.Empty, "no extraction is queued when the cascade fails");
    }

    [Test]
    public async Task Reprocess_SourceWithNoBatches_StillRequeues()
    {
        // A Failed source may have no batch at all (extraction never succeeded).
        var source = SeedSource(SourceProcessingStatus.Failed);

        var result = await _sut.ReprocessAsync(Command(source.Id), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.ProcessingStatus, Is.EqualTo(SourceProcessingStatus.Queued));
        Assert.That(_queueClient.SentMessages, Has.Count.EqualTo(1));
    }
}
