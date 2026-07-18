using Nornis.Application.Services;
using Nornis.Application.Tests.Fakes;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Services;

/// <summary>
/// The map read model: pins inherit the referenced artifact's visibility, and dangling
/// or archived-artifact pins drop out for every caller.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class MapViewServiceTests
{
    private static readonly Guid WorldId = Guid.NewGuid();
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid OtherPlayerId = Guid.NewGuid();

    private InMemorySourceRepository _sourceRepo = null!;
    private InMemorySourceAttachmentRepository _attachmentRepo = null!;
    private InMemoryMapPlacemarkRepository _placemarkRepo = null!;
    private InMemoryArtifactRepository _artifactRepo = null!;
    private FakeBlobStorageService _blob = null!;
    private MapViewService _sut = null!;

    private Source _source = null!;
    private SourceAttachment _map = null!;

    [SetUp]
    public void SetUp()
    {
        _sourceRepo = new InMemorySourceRepository();
        _attachmentRepo = new InMemorySourceAttachmentRepository();
        _placemarkRepo = new InMemoryMapPlacemarkRepository();
        _artifactRepo = new InMemoryArtifactRepository();
        _blob = new FakeBlobStorageService();

        _sut = new MapViewService(_sourceRepo, _attachmentRepo, _placemarkRepo, _artifactRepo, _blob);

        _source = new Source
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = SourceType.Map, Title = "Map",
            Visibility = VisibilityScope.PartyVisible, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = OwnerId, CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(_source);

        _map = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = _source.Id, WorldId = WorldId,
            Kind = SourceAttachmentKind.MapImage, FileName = "map.png", ContentType = "image/png",
            SizeBytes = 3, BlobPath = "b", Ord = 0, Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepo.Seed(_map);
    }

    private Artifact SeedLocation(string name, VisibilityScope visibility, Guid? owner = null, ArtifactStatus status = ArtifactStatus.Active)
    {
        var a = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = WorldId, Type = ArtifactType.Location, Name = name,
            Visibility = visibility, CreatedByUserId = owner, Status = status,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(a);
        return a;
    }

    private void SeedPin(Guid artifactId) => _placemarkRepo.Seed(new MapPlacemark
    {
        Id = Guid.NewGuid(), WorldId = WorldId, SourceAttachmentId = _map.Id, ArtifactId = artifactId,
        X = 0.5m, Y = 0.5m, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    });

    [Test]
    public async Task NoMap_ReturnsNotFound()
    {
        _attachmentRepo.DeleteAsync(_map.Id).GetAwaiter().GetResult();

        var result = await _sut.GetMapAsync(_source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("no_map"));
    }

    [Test]
    public async Task Player_SeesPartyPins_ButNotOthersPrivate()
    {
        var party = SeedLocation("Black Harbor", VisibilityScope.PartyVisible);
        var othersPrivate = SeedLocation("Secret Cove", VisibilityScope.Private, owner: OtherPlayerId);
        SeedPin(party.Id);
        SeedPin(othersPrivate.Id);

        var result = await _sut.GetMapAsync(_source.Id, WorldId, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        var names = result.Value!.Placemarks.Select(p => p.ArtifactName).ToList();
        Assert.That(names, Does.Contain("Black Harbor"));
        Assert.That(names, Does.Not.Contain("Secret Cove"));
    }

    [Test]
    public async Task ArchivedArtifactPin_IsDropped()
    {
        var archived = SeedLocation("Merged Away", VisibilityScope.PartyVisible, status: ArtifactStatus.Archived);
        SeedPin(archived.Id);

        var result = await _sut.GetMapAsync(_source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Placemarks, Is.Empty);
    }

    [Test]
    public async Task DanglingPin_IsDropped()
    {
        SeedPin(Guid.NewGuid()); // artifact never existed / hard-deleted

        var result = await _sut.GetMapAsync(_source.Id, WorldId, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.Value!.Placemarks, Is.Empty);
    }

    [Test]
    public async Task PrivateSource_NotVisibleToOtherPlayer_Returns404()
    {
        _source.Visibility = VisibilityScope.Private;

        var result = await _sut.GetMapAsync(_source.Id, WorldId, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }
}
