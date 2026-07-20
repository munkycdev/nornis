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

namespace Nornis.Api.Tests.Public;

/// <summary>
/// The journey over a world's map, read anonymously through the public slug. Runs as
/// Observer with the sentinel user id, so the trail an anonymous reader sees must be the
/// party-visible one — a GM-only session never becomes a stop.
/// </summary>
[TestFixture]
[Category("Feature: journey-map")]
public class PublicJourneyEndpointTests
{
    private const string Slug = "black-harbor";

    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        // No Authorization header — exercises [AllowAnonymous] against the real FallbackPolicy.
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private async Task PublishAsync(bool enabled = true)
    {
        var update = await _scenario.GmClient.PutAsJsonAsync($"/api/worlds/{_scenario.World.Id}",
            new UpdateWorldRequest(PublicSlug: Slug, PublicAccessEnabled: enabled));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), await update.Content.ReadAsStringAsync());
    }

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
    public async Task PublicJourney_ReturnsPinsAndStops_ForEnabledWorld()
    {
        var locationId = await SeedMapWithPinAsync("Black Harbor");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), VisibilityScope.PartyVisible, "Arrival");
        await PublishAsync();

        var response = await _anonymous.GetAsync($"/api/public/worlds/{Slug}/journey");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var journey = await response.Content.ReadFromJsonAsync<JourneyResponse>();
        Assert.That(journey!.Locations.Select(l => l.Name), Does.Contain("Black Harbor"));
        Assert.That(journey.Stops, Has.Count.EqualTo(1));
        Assert.That(journey.Stops[0].Title, Is.EqualTo("Arrival"));
        Assert.That(journey.Stops[0].VisitedLocationIds, Does.Contain(locationId));
    }

    [Test]
    public async Task PublicJourney_OmitsGmOnlySessions_FromTheAnonymousTrail()
    {
        var locationId = await SeedMapWithPinAsync("Black Harbor");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), VisibilityScope.PartyVisible, "Arrival");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero), VisibilityScope.GMOnly, "The betrayal");
        await PublishAsync();

        var response = await _anonymous.GetAsync($"/api/public/worlds/{Slug}/journey");

        var journey = await response.Content.ReadFromJsonAsync<JourneyResponse>();
        Assert.That(journey!.Stops.Select(s => s.Title), Is.EqualTo(new[] { "Arrival" }));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("betrayal"));
    }

    [Test]
    public async Task PublicJourney_ReportsNoMap_SoThePageCanShowItsEmptyState()
    {
        await PublishAsync();

        var response = await _anonymous.GetAsync($"/api/public/worlds/{Slug}/journey");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("no_map"));
    }

    [Test]
    public async Task PublicJourney_UnknownSlug_AndDisabledWorld_ReturnIdentical404s()
    {
        var locationId = await SeedMapWithPinAsync("Black Harbor");
        await SeedVisitingSessionAsync(
            locationId, new DateTimeOffset(2026, 1, 11, 0, 0, 0, TimeSpan.Zero), VisibilityScope.PartyVisible, "Arrival");
        await PublishAsync(enabled: false);

        var unknown = await _anonymous.GetAsync("/api/public/worlds/no-such-world/journey");
        var disabled = await _anonymous.GetAsync($"/api/public/worlds/{Slug}/journey");

        Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(disabled.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await unknown.Content.ReadAsStringAsync(), Is.EqualTo(await disabled.Content.ReadAsStringAsync()),
            "unknown and disabled must be indistinguishable — no existence oracle");
    }
}
