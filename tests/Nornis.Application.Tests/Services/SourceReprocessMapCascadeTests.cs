using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Models;
using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// Reprocess cascade for map pins: a reprocessed map source loses its own pins, and pins
/// on any map pointing at an artifact this reprocess hard-deletes are removed too. The
/// preview counts them.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class SourceReprocessMapCascadeTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepo = null!;
    private InMemoryReviewBatchRepository _batchRepo = null!;
    private InMemoryReviewProposalRepository _proposalRepo = null!;
    private InMemorySourceReferenceRepository _refRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemorySourceAttachmentRepository _attachmentRepo = null!;
    private InMemoryMapPlacemarkRepository _placemarkRepo = null!;
    private FakeExtractionQueueClient _queue = null!;
    private SourceReprocessService _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _batchRepo = new InMemoryReviewBatchRepository();
        _proposalRepo = new InMemoryReviewProposalRepository(_batchRepo);
        _refRepo = new InMemorySourceReferenceRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _attachmentRepo = new InMemorySourceAttachmentRepository();
        _placemarkRepo = new InMemoryMapPlacemarkRepository(_attachmentRepo); // wired for the source join
        _queue = new FakeExtractionQueueClient();

        _sut = new SourceReprocessService(
            _sourceRepo, _batchRepo, _proposalRepo, _refRepo, _artifactRepo,
            new InMemoryArtifactFactRepository(), new InMemoryArtifactRelationshipRepository(),
            new InMemoryCharacterRepository(), _placemarkRepo, _attachmentRepo,
            _queue, new FakeUnitOfWork(), NullLogger<SourceReprocessService>.Instance);
    }

    private (Source Source, SourceAttachment Map, Artifact Created, MapPlacemark Pin) SeedMapWithPin()
    {
        var source = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.Map, Title = "Realm",
            Visibility = VisibilityScope.PartyVisible, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = OwnerId, CreatedAt = DateTimeOffset.UtcNow, ExtractionEnabled = true
        };
        _sourceRepo.Seed(source);

        var map = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = source.Id, WorldId = WorldId,
            Kind = SourceAttachmentKind.MapImage, FileName = "map.png", ContentType = "image/png",
            SizeBytes = 3, BlobPath = "b", Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepo.Seed(map);

        var batch = new ReviewBatch
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceId = source.Id,
            Status = ReviewBatchStatus.Completed, CreatedAt = DateTimeOffset.UtcNow
        };
        _batchRepo.CreateAsync(batch).GetAwaiter().GetResult();

        // The source created a Location, pinned it, and nothing else touched it → orphan.
        var created = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Location, Name = "Ironhold",
            Visibility = VisibilityScope.PartyVisible, Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(created);
        _proposalRepo.CreateAsync(new ReviewProposal
        {
            Id = Guid.NewGuid(), ReviewBatchId = batch.Id, ChangeType = ReviewChangeType.CreateArtifact,
            TargetType = ReviewTargetType.Artifact, TargetId = created.Id, ProposedValueJson = "{}",
            Rationale = "test", Status = ReviewProposalStatus.Accepted, CreatedAt = DateTimeOffset.UtcNow
        }).GetAwaiter().GetResult();
        _refRepo.Seed(new SourceReference
        {
            Id = Guid.NewGuid(), SourceId = source.Id, TargetType = SourceReferenceTargetType.Artifact,
            TargetId = created.Id, CreatedAt = DateTimeOffset.UtcNow
        });

        var pin = new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceAttachmentId = map.Id, ArtifactId = created.Id,
            X = 0.4m, Y = 0.6m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _placemarkRepo.Seed(pin);

        return (source, map, created, pin);
    }

    [Test]
    public async Task Reprocess_DeletesThisSourcesPins()
    {
        var scenario = SeedMapWithPin();

        var result = await _sut.ReprocessAsync(
            new ReprocessSourceCommand(scenario.Source.Id, WorldId, OwnerId, WorldRole.GM, Body: null),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks, Is.Empty, "the map's pins are torn down for re-extraction");
    }

    [Test]
    public async Task Reprocess_DeletesForeignMapPin_WhenArtifactHardDeleted()
    {
        var scenario = SeedMapWithPin();

        // A DIFFERENT source's map also pins the artifact this reprocess will hard-delete.
        var otherAttachmentId = Guid.NewGuid();
        _placemarkRepo.Seed(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = WorldId, SourceAttachmentId = otherAttachmentId,
            ArtifactId = scenario.Created.Id, X = 0.1m, Y = 0.1m,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });

        var result = await _sut.ReprocessAsync(
            new ReprocessSourceCommand(scenario.Source.Id, WorldId, OwnerId, WorldRole.GM, Body: null),
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_artifactRepo.Artifacts.Any(a => a.Id == scenario.Created.Id), Is.False, "orphan artifact deleted");
        Assert.That(_placemarkRepo.Placemarks, Is.Empty, "the foreign map's pin to the deleted artifact is also removed");
    }

    [Test]
    public async Task Preview_CountsPinsToDelete()
    {
        var scenario = SeedMapWithPin();

        var result = await _sut.PreviewAsync(scenario.Source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.MapPinsToDelete, Is.EqualTo(1));
    }
}
