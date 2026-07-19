using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

/// <summary>
/// The journey endpoint end-to-end (real EF-InMemory + auth + controllers): a world map's pins
/// plus the visible dated sessions that visited them, with the same visibility split the map
/// viewer has — a player never sees a GM-only session's stop.
/// </summary>
[TestFixture]
[Category("Feature: journey-map")]
public class JourneyEndpointTests
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

    private async Task<Guid> SeedMapWithPinAsync(string locationName)
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
        var location = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = ArtifactType.Location, Name = locationName,
            Visibility = VisibilityScope.PartyVisible, Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        db.Sources.Add(source);
        db.SourceAttachments.Add(map);
        db.Artifacts.Add(location);
        db.MapPlacemarks.Add(new MapPlacemark
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, SourceAttachmentId = map.Id,
            ArtifactId = location.Id, X = 0.5m, Y = 0.5m, Label = locationName, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
        return location.Id;
    }

    private async Task SeedVisitingSessionAsync(
        Guid locationId, DateTimeOffset occurredAt, VisibilityScope visibility, string title)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;

        var session = new Source
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = SourceType.SessionNote, Title = title,
            Visibility = visibility, ProcessingStatus = SourceProcessingStatus.Processed,
            CreatedByUserId = _scenario.GmUserId, CreatedAt = now, OccurredAt = occurredAt
        };
        db.Sources.Add(session);
        db.SourceReferences.Add(new SourceReference
        {
            Id = Guid.NewGuid(), SourceId = session.Id, TargetType = SourceReferenceTargetType.Artifact,
            TargetId = locationId, CreatedAt = now
        });
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task GetJourney_ReturnsPinsAndStops()
    {
        var locationId = await SeedMapWithPinAsync("Black Harbor");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), VisibilityScope.PartyVisible, "Arrival");

        var response = await _scenario.GmClient.GetAsync($"/api/worlds/{_scenario.World.Id}/journey");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var journey = await response.Content.ReadFromJsonAsync<JourneyResponse>();
        Assert.That(journey!.Locations.Select(l => l.Name), Does.Contain("Black Harbor"));
        Assert.That(journey.Stops, Has.Count.EqualTo(1));
        Assert.That(journey.Stops[0].Title, Is.EqualTo("Arrival"));
        Assert.That(journey.Stops[0].VisitedLocationIds, Does.Contain(locationId));
    }

    [Test]
    public async Task GetJourney_NoMap_Returns404()
    {
        var response = await _scenario.GmClient.GetAsync($"/api/worlds/{_scenario.World.Id}/journey");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetJourney_Player_DoesNotSeeGmOnlySessionStop()
    {
        var locationId = await SeedMapWithPinAsync("Black Harbor");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), VisibilityScope.PartyVisible, "Public session");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 2, 15, 0, 0, 0, TimeSpan.Zero), VisibilityScope.GMOnly, "GM session");

        var gm = await (await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/journey")).Content.ReadFromJsonAsync<JourneyResponse>();
        var player = await (await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/journey")).Content.ReadFromJsonAsync<JourneyResponse>();

        Assert.That(gm!.Stops, Has.Count.EqualTo(2));
        Assert.That(player!.Stops, Has.Count.EqualTo(1));
        Assert.That(player.Stops[0].Title, Is.EqualTo("Public session"));
    }
}
