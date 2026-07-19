using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Reveal;

/// <summary>
/// Source reveal (feature 17, phase 2) end-to-end over real controller/auth/EF: a GM lifts a
/// GM-only source to the party, after which players can load it — the map worked example —
/// while GM-only pins on it stay hidden until separately revealed. Non-GMs are forbidden.
/// </summary>
[TestFixture]
[Category("Feature: knowledge-reveal")]
public class RevealSourceEndpointTests
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

    private string RevealUrl(Guid sourceId) => $"/api/worlds/{_scenario.World.Id}/sources/{sourceId}/reveal";
    private string MapUrl(Guid sourceId) => $"/api/worlds/{_scenario.World.Id}/sources/{sourceId}/map";
    private string SourceUrl(Guid sourceId) => $"/api/worlds/{_scenario.World.Id}/sources/{sourceId}";

    private async Task<(Source Source, SourceAttachment Map)> SeedGmOnlyMapAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var source = new Source
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = SourceType.Map, Title = "The GM's master map",
            Visibility = VisibilityScope.GMOnly, ProcessingStatus = SourceProcessingStatus.Processed,
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

    private async Task SeedPinAsync(Guid mapAttachmentId, string name, VisibilityScope visibility)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = ArtifactType.Location, Name = name,
            Visibility = visibility, Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
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
    public async Task RevealSource_Gm_MakesGmOnlyMapVisibleToPlayer_ButKeepsGmOnlyPinHidden()
    {
        var (source, map) = await SeedGmOnlyMapAsync();
        await SeedPinAsync(map.Id, "Harbor Town", VisibilityScope.PartyVisible);
        await SeedPinAsync(map.Id, "Hidden Shrine", VisibilityScope.GMOnly);

        // Before: the player cannot see the GM-only map at all.
        var before = await _scenario.PlayerClient.GetAsync(MapUrl(source.Id));
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        var reveal = await _scenario.GmClient.PostAsync(RevealUrl(source.Id), null);
        Assert.That(reveal.StatusCode, Is.EqualTo(HttpStatusCode.OK), await reveal.Content.ReadAsStringAsync());

        // After: the player can load the map; the party-visible pin shows, the GM-only pin does not.
        var afterResp = await _scenario.PlayerClient.GetAsync(MapUrl(source.Id));
        Assert.That(afterResp.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await afterResp.Content.ReadAsStringAsync();
        Assert.That(body, Does.Contain("Harbor Town"));
        Assert.That(body, Does.Not.Contain("Hidden Shrine"), "a GM-only location stays hidden until separately revealed");
    }

    [Test]
    public async Task RevealSource_Player_IsForbidden()
    {
        var (source, _) = await SeedGmOnlyMapAsync();

        var response = await _scenario.PlayerClient.PostAsync(RevealUrl(source.Id), null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RevealSource_AlreadyPartyVisible_IsNoOp_200()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            visibility: VisibilityScope.PartyVisible, processingStatus: SourceProcessingStatus.Processed);

        var response = await _scenario.GmClient.PostAsync(RevealUrl(source.Id), null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task RevealSource_Gm_GmOnlyTextSource_ThenVisibleToPlayer()
    {
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, _scenario.GmUserId,
            title: "Villain's true plan", visibility: VisibilityScope.GMOnly,
            processingStatus: SourceProcessingStatus.Processed);

        var before = await _scenario.PlayerClient.GetAsync(SourceUrl(source.Id));
        Assert.That(before.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "GM-only source is hidden from the player");

        var reveal = await _scenario.GmClient.PostAsync(RevealUrl(source.Id), null);
        Assert.That(reveal.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await _scenario.PlayerClient.GetAsync(SourceUrl(source.Id));
        Assert.That(after.StatusCode, Is.EqualTo(HttpStatusCode.OK), "player sees it once revealed");
    }
}
