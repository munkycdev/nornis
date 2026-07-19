using System.Text.Json;
using Nornis.Application.Application;
using Nornis.Application.Tests.Fakes;
using Nornis.Application.Validation;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Application;

/// <summary>
/// Applying map proposals: CreateArtifact-with-pin makes artifact + placemark
/// atomically; AddPlacemark resolves by id/name and upserts; merge reassigns pins with
/// unique-key collision handling; bad attachments fail the apply.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class ProposalApplicatorPlacemarkTests
{
    private InMemoryArtifactRepository _artifactRepo = null!;
    private InMemoryArtifactFactRepository _factRepo = null!;
    private InMemoryArtifactRelationshipRepository _relationshipRepo = null!;
    private InMemorySourceReferenceRepository _sourceRefRepo = null!;
    private InMemorySourceRepository _sourceRepo = null!;
    private InMemorySourceAttachmentRepository _attachmentRepo = null!;
    private InMemoryMapPlacemarkRepository _placemarkRepo = null!;
    private ProposalApplicator _applicator = null!;

    private Guid _worldId;
    private Source _source = null!;
    private ReviewBatch _batch = null!;
    private SourceAttachment _mapAttachment = null!;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [SetUp]
    public void SetUp()
    {
        _artifactRepo = new InMemoryArtifactRepository();
        _factRepo = new InMemoryArtifactFactRepository();
        _relationshipRepo = new InMemoryArtifactRelationshipRepository();
        _sourceRefRepo = new InMemorySourceReferenceRepository();
        _sourceRepo = new InMemorySourceRepository();
        _attachmentRepo = new InMemorySourceAttachmentRepository();
        _placemarkRepo = new InMemoryMapPlacemarkRepository();

        _applicator = new ProposalApplicator(
            _artifactRepo, _factRepo, _relationshipRepo, _sourceRefRepo, _sourceRepo,
            _attachmentRepo, _placemarkRepo);

        _worldId = Guid.NewGuid();
        _source = new Source
        {
            Id = Guid.NewGuid(), WorldId = _worldId, Type = SourceType.Map, Title = "Realm map",
            Visibility = VisibilityScope.PartyVisible, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow.AddHours(-1)
        };
        _sourceRepo.Seed(_source);

        _batch = new ReviewBatch
        {
            Id = Guid.NewGuid(), WorldId = _worldId, SourceId = _source.Id,
            Status = ReviewBatchStatus.InReview, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-30)
        };

        _mapAttachment = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = _source.Id, WorldId = _worldId,
            Kind = SourceAttachmentKind.MapImage, FileName = "map.png", ContentType = "image/png",
            SizeBytes = 3, BlobPath = "b", Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepo.Seed(_mapAttachment);
    }

    private ReviewProposal Proposal(ReviewChangeType type, Guid? targetId, object payload) => new()
    {
        Id = Guid.NewGuid(), ReviewBatchId = _batch.Id, ChangeType = type,
        TargetType = ReviewTargetType.Artifact, TargetId = targetId,
        ProposedValueJson = JsonSerializer.Serialize(payload, JsonOptions),
        Rationale = "test", Status = ReviewProposalStatus.Pending, CreatedAt = DateTimeOffset.UtcNow
    };

    private Artifact SeedLocation(string name)
    {
        var a = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _worldId, Type = ArtifactType.Location, Name = name,
            Visibility = VisibilityScope.PartyVisible, Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(a);
        return a;
    }

    [Test]
    public async Task CreateArtifactWithPin_MakesArtifactAndPlacemark()
    {
        var proposal = Proposal(ReviewChangeType.CreateArtifact, null, new
        {
            name = "Ironhold", type = "Location", summary = "A fortress marked on the map.",
            mapPlacemark = new { attachmentId = _mapAttachment.Id, x = 0.4m, y = 0.6m, label = "Ironhold" }
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var artifact = _artifactRepo.Artifacts.Single(a => a.Name == "Ironhold");
        var pin = _placemarkRepo.Placemarks.Single();
        Assert.That(pin.ArtifactId, Is.EqualTo(artifact.Id));
        Assert.That(pin.SourceAttachmentId, Is.EqualTo(_mapAttachment.Id));
        Assert.That(pin.X, Is.EqualTo(0.4m));
    }

    [Test]
    public async Task CreateArtifactWithPin_WrongSourceAttachment_FailsApply()
    {
        var foreignAttachment = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = Guid.NewGuid(), WorldId = _worldId,
            Kind = SourceAttachmentKind.MapImage, FileName = "other.png", ContentType = "image/png",
            SizeBytes = 3, BlobPath = "x", Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepo.Seed(foreignAttachment);

        var proposal = Proposal(ReviewChangeType.CreateArtifact, null, new
        {
            name = "Ironhold", type = "Location",
            mapPlacemark = new { attachmentId = foreignAttachment.Id, x = 0.4m, y = 0.6m, label = "Ironhold" }
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public async Task AddPlacemark_ByTargetId_CreatesPin()
    {
        var ironhold = SeedLocation("Ironhold");
        var proposal = Proposal(ReviewChangeType.AddPlacemark, ironhold.Id, new
        {
            artifactId = ironhold.Id, attachmentId = _mapAttachment.Id, x = 0.3m, y = 0.7m, label = "Ironhold"
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks.Single().ArtifactId, Is.EqualTo(ironhold.Id));
    }

    [Test]
    public async Task AddPlacemark_ByName_ResolvesUniqueArtifact()
    {
        var ironhold = SeedLocation("Ironhold");
        var proposal = Proposal(ReviewChangeType.AddPlacemark, null, new
        {
            artifactName = "Ironhold", attachmentId = _mapAttachment.Id, x = 0.3m, y = 0.7m, label = "Ironhold"
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks.Single().ArtifactId, Is.EqualTo(ironhold.Id));
    }

    [Test]
    public async Task AddPlacemark_AmbiguousName_Fails()
    {
        SeedLocation("Duplicate");
        SeedLocation("Duplicate");
        var proposal = Proposal(ReviewChangeType.AddPlacemark, null, new
        {
            artifactName = "Duplicate", attachmentId = _mapAttachment.Id, x = 0.3m, y = 0.7m, label = "Duplicate"
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
    }

    [Test]
    public async Task AddPlacemark_ExistingPin_UpsertsInPlace()
    {
        var ironhold = SeedLocation("Ironhold");
        _placemarkRepo.Seed(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = _worldId, SourceAttachmentId = _mapAttachment.Id,
            ArtifactId = ironhold.Id, X = 0.1m, Y = 0.1m,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });

        var proposal = Proposal(ReviewChangeType.AddPlacemark, ironhold.Id, new
        {
            artifactId = ironhold.Id, attachmentId = _mapAttachment.Id, x = 0.8m, y = 0.9m, label = "Ironhold"
        });

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var pin = _placemarkRepo.Placemarks.Single();
        Assert.That(pin.X, Is.EqualTo(0.8m), "existing pin updated, not duplicated");
    }

    [Test]
    public async Task Merge_ReassignsSourceArtifactPins_ToTarget()
    {
        var target = SeedLocation("Ironhold");
        var duplicate = SeedLocation("Iron Hold");
        var pin = new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = _worldId, SourceAttachmentId = _mapAttachment.Id,
            ArtifactId = duplicate.Id, X = 0.5m, Y = 0.5m,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _placemarkRepo.Seed(pin);

        var proposal = Proposal(ReviewChangeType.MergeArtifact, target.Id,
            new MergeArtifactPayload(duplicate.Id, null, null, null, null));

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks.Single().ArtifactId, Is.EqualTo(target.Id));
    }

    [Test]
    public async Task Merge_PinCollisionOnSameMap_DropsSourcePin()
    {
        var target = SeedLocation("Ironhold");
        var duplicate = SeedLocation("Iron Hold");
        // Both pinned on the same map — the target's pin wins, the duplicate's is dropped.
        _placemarkRepo.Seed(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = _worldId, SourceAttachmentId = _mapAttachment.Id,
            ArtifactId = target.Id, X = 0.5m, Y = 0.5m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });
        _placemarkRepo.Seed(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = _worldId, SourceAttachmentId = _mapAttachment.Id,
            ArtifactId = duplicate.Id, X = 0.6m, Y = 0.6m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        });

        var proposal = Proposal(ReviewChangeType.MergeArtifact, target.Id,
            new MergeArtifactPayload(duplicate.Id, null, null, null, null));

        var result = await _applicator.ApplyAsync(proposal, _batch, VisibilityFilter.All, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks, Has.Count.EqualTo(1), "no duplicate pin on the same map");
        Assert.That(_placemarkRepo.Placemarks.Single().ArtifactId, Is.EqualTo(target.Id));
    }
}
