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
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = SourceType.Map, Title = "Realm map",
            Visibility = VisibilityScope.PartyVisible, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _scenario.GmUserId, CreatedAt = now
        };
        var map = new SourceAttachment
        {
            Id = Guid.NewGuid(), SourceId = source.Id, WorldId = _scenario.World.Id,
            Kind = SourceAttachmentKind.MapImage, FileName = "map.png", ContentType = "image/png",
            SizeBytes = 10, BlobPath = $"worlds/{_scenario.World.Id}/sources/{source.Id}/000-map.png",
            Ord = 0, Status = SourceAttachmentStatus.Stored, CreatedAt = now, UpdatedAt = now
        };
        db.Sources.Add(source);
        db.SourceAttachments.Add(map);
        await db.SaveChangesAsync();
        return (source, map);
    }

    private async Task SeedPinnedLocationAsync(Guid mapAttachmentId, string name, VisibilityScope visibility, Guid? owner = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = ArtifactType.Location, Name = name,
            Visibility = visibility, CreatedByUserId = owner, Status = ArtifactStatus.Active,
            CreatedAt = now, UpdatedAt = now
        };
        db.Artifacts.Add(artifact);
        db.MapPlacemarks.Add(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, SourceAttachmentId = mapAttachmentId,
            ArtifactId = artifact.Id, X = 0.5m, Y = 0.5m, Label = name, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
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
