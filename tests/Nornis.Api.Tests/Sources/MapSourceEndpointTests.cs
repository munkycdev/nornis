using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

/// <summary>
/// Map source endpoints end-to-end (real EF-InMemory + auth + controllers): the
/// map-view read with visibility-filtered pins, and the new attachment kinds.
/// </summary>
[TestFixture]
[Category("Feature: map-source")]
public class MapSourceEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<(Source Source, SourceAttachment Map)> SeedMapSourceAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;

        var source = new Source
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Type = SourceType.Map,
            Title = "Realm map",
            Visibility = VisibilityScope.PartyVisible,
            ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _scenario.GmUserId,
            CreatedAt = now
        };
        var map = new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = source.Id,
            WorldId = _scenario.World.Id,
            Kind = SourceAttachmentKind.MapImage,
            FileName = "map.png",
            ContentType = "image/png",
            SizeBytes = 10,
            BlobPath = $"worlds/{_scenario.World.Id}/sources/{source.Id}/000-map.png",
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Sources.Add(source);
        db.SourceAttachments.Add(map);
        await db.SaveChangesAsync();
        return (source, map);
    }

    private async Task<Guid> SeedPinnedLocationAsync(Guid mapAttachmentId, string name, VisibilityScope visibility, Guid? owner = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Type = ArtifactType.Location,
            Name = name,
            Visibility = visibility,
            CreatedByUserId = owner,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Artifacts.Add(artifact);
        var placemark = new MapPlacemark
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            SourceAttachmentId = mapAttachmentId,
            ArtifactId = artifact.Id,
            X = 0.5m,
            Y = 0.5m,
            Label = name,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.MapPlacemarks.Add(placemark);
        await db.SaveChangesAsync();
        return placemark.Id;
    }

    [Test]
    public async Task GetMap_ReturnsImageUrlAndPins()
    {
        var (source, map) = await SeedMapSourceAsync();
        await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var map_ = await response.Content.ReadFromJsonAsync<MapViewResponse>();
        Assert.That(map_!.ImageUrl, Does.Contain("sas=download"));
        Assert.That(map_.Placemarks, Has.Count.EqualTo(1));
        Assert.That(map_.Placemarks[0].ArtifactName, Is.EqualTo("Ironhold"));
    }

    [Test]
    public async Task GetMap_Player_DoesNotSeeAnotherUsersPrivatePin()
    {
        var (source, map) = await SeedMapSourceAsync();
        await SeedPinnedLocationAsync(map.Id, "Public Place", VisibilityScope.PartyVisible);
        await SeedPinnedLocationAsync(map.Id, "GM Secret", VisibilityScope.GMOnly);

        var body = await (await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("Public Place"));
        Assert.That(body, Does.Not.Contain("GM Secret"), "GMOnly location's pin is filtered for the player");
    }

    [Test]
    public async Task GetMap_NoMap_404()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            type: SourceType.Map, processingStatus: SourceProcessingStatus.Draft);

        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Attachment_MapImageOnMapSource_HandshakeWorks()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            type: SourceType.Map, processingStatus: SourceProcessingStatus.Draft);

        var ticketResponse = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/attachments/request-upload",
            new RequestSourceAttachmentUploadRequest("realm.png", "image/png", 8000, "MapImage"));
        Assert.That(ticketResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ticketResponse.Content.ReadAsStringAsync());
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<SourceAttachmentUploadResponse>();

        // BuildSourceBlobPath convention: worlds/{world}/sources/{source}/{ord:D3}-{file}.
        _factory.BlobStorage.Blobs[$"worlds/{_scenario.World.Id}/sources/{source.Id}/000-realm.png"] =
            (new byte[8000], "image/png");
        var confirm = await _scenario.GmClient.PostAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/attachments/{ticket!.Attachment.Id}/confirm", null);

        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Attachment_DocumentPdf_OnUploadSource_Works()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            type: SourceType.Upload, processingStatus: SourceProcessingStatus.Draft);

        var ticketResponse = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/attachments/request-upload",
            new RequestSourceAttachmentUploadRequest("handout.pdf", "application/pdf", 5000, "Document"));

        Assert.That(ticketResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ticketResponse.Content.ReadAsStringAsync());
    }

    private async Task<Guid> SeedLocationAsync(string name, VisibilityScope visibility = VisibilityScope.PartyVisible,
        ArtifactType type = ArtifactType.Location)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Type = type,
            Name = name,
            Visibility = visibility,
            Status = ArtifactStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Artifacts.Add(artifact);
        await db.SaveChangesAsync();
        return artifact.Id;
    }

    [Test]
    public async Task CreatePlacemark_Gm_PinsLocationAtCentreAndPersists()
    {
        var (source, map) = await SeedMapSourceAsync();
        var artifactId = await SeedLocationAsync("Thistle Hold");

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks",
            new CreatePlacemarkRequest(artifactId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var pin = await response.Content.ReadFromJsonAsync<MapPlacemarkResponse>();
        Assert.That(pin!.X, Is.EqualTo(0.5m));
        Assert.That(pin.Y, Is.EqualTo(0.5m));
        Assert.That(pin.ArtifactName, Is.EqualTo("Thistle Hold"));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var stored = db.MapPlacemarks.Single(p => p.Id == pin.Id);
        Assert.That(stored.SourceAttachmentId, Is.EqualTo(map.Id));
        Assert.That(stored.ArtifactId, Is.EqualTo(artifactId));
    }

    [Test]
    public async Task CreatePlacemark_AlreadyPinned_409()
    {
        var (source, map) = await SeedMapSourceAsync();
        await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var artifactId = db.Artifacts.Single(a => a.Name == "Ironhold").Id;

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks",
            new CreatePlacemarkRequest(artifactId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task CreatePlacemark_NonLocationArtifact_400()
    {
        var (source, _) = await SeedMapSourceAsync();
        var artifactId = await SeedLocationAsync("Sera", type: ArtifactType.Character);

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks",
            new CreatePlacemarkRequest(artifactId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
    }

    [Test]
    public async Task CreatePlacemark_PlayerWhoIsNotCreator_403()
    {
        var (source, _) = await SeedMapSourceAsync(); // created by the GM
        var artifactId = await SeedLocationAsync("Thistle Hold");

        var response = await _scenario.PlayerClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks",
            new CreatePlacemarkRequest(artifactId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.MapPlacemarks.Any(), Is.False);
    }

    [Test]
    public async Task CreatePlacemark_UnknownArtifact_404()
    {
        var (source, _) = await SeedMapSourceAsync();

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks",
            new CreatePlacemarkRequest(Guid.NewGuid()));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task MovePlacemark_Gm_UpdatesPositionAndPersists()
    {
        var (source, map) = await SeedMapSourceAsync();
        var pinId = await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        var response = await _scenario.GmClient.PatchAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks/{pinId}",
            new MovePlacemarkRequest(0.25m, 0.75m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var moved = await response.Content.ReadFromJsonAsync<MapPlacemarkResponse>();
        Assert.That(moved!.X, Is.EqualTo(0.25m));
        Assert.That(moved.Y, Is.EqualTo(0.75m));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var stored = db.MapPlacemarks.Single(p => p.Id == pinId);
        Assert.That(stored.X, Is.EqualTo(0.25m));
        Assert.That(stored.Y, Is.EqualTo(0.75m));
    }

    [Test]
    public async Task MovePlacemark_PlayerWhoIsNotCreator_403()
    {
        var (source, map) = await SeedMapSourceAsync(); // created by the GM
        var pinId = await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        var response = await _scenario.PlayerClient.PatchAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks/{pinId}",
            new MovePlacemarkRequest(0.25m, 0.75m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task MovePlacemark_OutOfRange_400()
    {
        var (source, map) = await SeedMapSourceAsync();
        var pinId = await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        var response = await _scenario.GmClient.PatchAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks/{pinId}",
            new MovePlacemarkRequest(1.5m, 0.5m));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RemovePlacemark_Gm_DeletesPinButKeepsArtifact()
    {
        var (source, map) = await SeedMapSourceAsync();
        var pinId = await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        var response = await _scenario.GmClient.DeleteAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks/{pinId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.MapPlacemarks.Any(p => p.Id == pinId), Is.False);
        Assert.That(db.Artifacts.Any(a => a.Name == "Ironhold"), Is.True,
            "removing a pin must never delete the Location artifact");
    }

    [Test]
    public async Task RemovePlacemark_PlayerWhoIsNotCreator_403()
    {
        var (source, map) = await SeedMapSourceAsync(); // created by the GM
        var pinId = await SeedPinnedLocationAsync(map.Id, "Ironhold", VisibilityScope.PartyVisible);

        var response = await _scenario.PlayerClient.DeleteAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/map/placemarks/{pinId}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        Assert.That(db.MapPlacemarks.Any(p => p.Id == pinId), Is.True);
    }

    [Test]
    public async Task Attachment_MapImageOnNonMapSource_400()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            type: SourceType.Image, processingStatus: SourceProcessingStatus.Draft);

        var response = await _scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}/attachments/request-upload",
            new RequestSourceAttachmentUploadRequest("map.png", "image/png", 5000, "MapImage"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }
}
