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
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = SourceType.Map,
            Title = "Map",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = OwnerId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _sourceRepo.Seed(_source);

        _map = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = _source.Id,
            WorldId = WorldId,
            Kind = SourceAttachmentKind.MapImage,
            FileName = "map.png",
            ContentType = "image/png",
            SizeBytes = 3,
            BlobPath = "b",
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _attachmentRepo.Seed(_map);
    }

    private Artifact SeedLocation(string name, VisibilityScope visibility, Guid? owner = null, ArtifactStatus status = ArtifactStatus.Active)
    {
        var a = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Location,
            Name = name,
            Visibility = visibility,
            CreatedByUserId = owner,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(a);
        return a;
    }

    private void SeedPin(Guid artifactId) => _placemarkRepo.Seed(new MapPlacemark
    {
        Id = Guid.NewGuid(),
        WorldId = WorldId,
        SourceAttachmentId = _map.Id,
        ArtifactId = artifactId,
        X = 0.5m,
        Y = 0.5m,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
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

    // ------------------------------------------------------------- add pin --

    [Test]
    public async Task CreatePlacemark_Creator_PinsLocationAtCentre()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, location.Id, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.X, Is.EqualTo(0.5m));
        Assert.That(result.Value.Y, Is.EqualTo(0.5m));
        Assert.That(result.Value.ArtifactName, Is.EqualTo("Thistle Hold"));
        Assert.That(result.Value.Label, Is.EqualTo("Thistle Hold"));
        Assert.That(result.Value.Confidence, Is.Null, "a human placed this pin — no model confidence");

        var stored = _placemarkRepo.Placemarks.Single();
        Assert.That(stored.SourceAttachmentId, Is.EqualTo(_map.Id));
        Assert.That(stored.ArtifactId, Is.EqualTo(location.Id));
        Assert.That(stored.WorldId, Is.EqualTo(WorldId));
    }

    [Test]
    public async Task CreatePlacemark_Gm_CanPinOnAnyonesMap()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, location.Id, OtherPlayerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task CreatePlacemark_OtherPlayer_Forbidden()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, location.Id, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"));
        Assert.That(_placemarkRepo.Placemarks, Is.Empty);
    }

    [Test]
    public async Task CreatePlacemark_Observer_Forbidden()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, location.Id, OwnerId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
    }

    [Test]
    public async Task CreatePlacemark_NonLocationArtifact_400()
    {
        var npc = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            Type = ArtifactType.Character,
            Name = "Sera",
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _artifactRepo.Seed(npc);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, npc.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(400));
        Assert.That(result.Error.Code, Is.EqualTo("invalid_artifact_type"));
        Assert.That(_placemarkRepo.Placemarks, Is.Empty);
    }

    [Test]
    public async Task CreatePlacemark_AlreadyPinned_409()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);
        SeedPin(location.Id);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, location.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(409));
        Assert.That(result.Error.Code, Is.EqualTo("already_pinned"));
        Assert.That(_placemarkRepo.Placemarks, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CreatePlacemark_ArchivedLocation_404()
    {
        var archived = SeedLocation("Merged Away", VisibilityScope.PartyVisible, status: ArtifactStatus.Archived);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, archived.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task CreatePlacemark_LocationHiddenFromCaller_404()
    {
        var hidden = SeedLocation("GM Secret", VisibilityScope.GMOnly);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, hidden.Id, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_placemarkRepo.Placemarks, Is.Empty);
    }

    [Test]
    public async Task CreatePlacemark_SourceWithNoStoredMap_404()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);
        await _attachmentRepo.DeleteAsync(_map.Id);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, WorldId, location.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("no_map"));
    }

    [Test]
    public async Task CreatePlacemark_SourceInAnotherWorld_404()
    {
        var location = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);

        var result = await _sut.CreatePlacemarkAsync(
            _source.Id, Guid.NewGuid(), location.Id, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ------------------------------------------------------------ move pin --

    private MapPlacemark SeedMovablePin(out Artifact artifact)
    {
        artifact = SeedLocation("Thistle Hold", VisibilityScope.PartyVisible);
        var pin = new MapPlacemark
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceAttachmentId = _map.Id,
            ArtifactId = artifact.Id,
            X = 0.5m,
            Y = 0.5m,
            Label = "Thistle Hold",
            Confidence = 0.9m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _placemarkRepo.Seed(pin);
        return pin;
    }

    [Test]
    public async Task MovePlacemark_Creator_UpdatesPosition()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, 0.25m, 0.75m, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value!.X, Is.EqualTo(0.25m));
        Assert.That(result.Value.Y, Is.EqualTo(0.75m));
        Assert.That(result.Value.ArtifactName, Is.EqualTo("Thistle Hold"));
        var stored = _placemarkRepo.Placemarks.Single(p => p.Id == pin.Id);
        Assert.That(stored.X, Is.EqualTo(0.25m));
        Assert.That(stored.Y, Is.EqualTo(0.75m));
    }

    [Test]
    public async Task MovePlacemark_Gm_CanMoveAnyonesPin()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, 0.1m, 0.2m, OtherPlayerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
    }

    [Test]
    public async Task MovePlacemark_OtherPlayer_Forbidden()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, 0.1m, 0.2m, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"));
    }

    [Test]
    public async Task MovePlacemark_Observer_Forbidden()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, 0.1m, 0.2m, OwnerId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
    }

    [TestCase(-0.01, 0.5)]
    [TestCase(1.01, 0.5)]
    [TestCase(0.5, -0.01)]
    [TestCase(0.5, 1.01)]
    public async Task MovePlacemark_OutOfRange_Rejected(double x, double y)
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, (decimal)x, (decimal)y, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("invalid_position"));
    }

    [Test]
    public async Task MovePlacemark_UnknownPin_404()
    {
        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, Guid.NewGuid(), 0.5m, 0.5m, OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task MovePlacemark_PinOnArtifactHiddenFromMover_404()
    {
        // The creator is a Player; a GMOnly artifact's pin never rendered for them.
        var hidden = SeedLocation("GM Secret", VisibilityScope.GMOnly);
        var pin = new MapPlacemark
        {
            Id = Guid.NewGuid(),
            WorldId = WorldId,
            SourceAttachmentId = _map.Id,
            ArtifactId = hidden.Id,
            X = 0.5m,
            Y = 0.5m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _placemarkRepo.Seed(pin);

        var result = await _sut.MovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, 0.1m, 0.2m, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    // ---------------------------------------------------------- remove pin --

    [Test]
    public async Task RemovePlacemark_Creator_DeletesPinButKeepsArtifact()
    {
        var pin = SeedMovablePin(out var artifact);

        var result = await _sut.RemovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks, Is.Empty);
        Assert.That(await _artifactRepo.GetByIdAsync(artifact.Id), Is.Not.Null,
            "removing a pin must never touch the Location artifact");
    }

    [Test]
    public async Task RemovePlacemark_Gm_CanRemoveAnyonesPin()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.RemovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, OtherPlayerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_placemarkRepo.Placemarks, Is.Empty);
    }

    [Test]
    public async Task RemovePlacemark_OtherPlayer_Forbidden()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.RemovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, OtherPlayerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("forbidden"));
        Assert.That(_placemarkRepo.Placemarks, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RemovePlacemark_Observer_Forbidden()
    {
        var pin = SeedMovablePin(out _);

        var result = await _sut.RemovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, OwnerId, WorldRole.Observer, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Code, Is.EqualTo("insufficient_role"));
    }

    [Test]
    public async Task RemovePlacemark_UnknownPin_404()
    {
        var result = await _sut.RemovePlacemarkAsync(
            _source.Id, WorldId, Guid.NewGuid(), OwnerId, WorldRole.GM, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
    }

    [Test]
    public async Task RemovePlacemark_PinOnArtifactHiddenFromRemover_404()
    {
        // The creator is a Player; a GMOnly artifact's pin never rendered for them.
        var hidden = SeedLocation("GM Secret", VisibilityScope.GMOnly);
        SeedPin(hidden.Id);
        var pin = _placemarkRepo.Placemarks.Single();

        var result = await _sut.RemovePlacemarkAsync(
            _source.Id, WorldId, pin.Id, OwnerId, WorldRole.Player, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.StatusCode, Is.EqualTo(404));
        Assert.That(_placemarkRepo.Placemarks, Has.Count.EqualTo(1));
    }
}
