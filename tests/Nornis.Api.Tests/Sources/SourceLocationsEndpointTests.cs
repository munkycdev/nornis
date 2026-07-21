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
/// The session→location link endpoints end-to-end (real EF-InMemory + auth + controllers): a
/// person marks where a session took place, only the creator or a GM may edit, and the read is
/// visibility-honest — a player never sees a GM-only place a session was linked to.
/// </summary>
[TestFixture]
[Category("Feature: locations")]
public class SourceLocationsEndpointTests
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

    private string Route(Guid sourceId) => $"/api/worlds/{_scenario.World.Id}/sources/{sourceId}/locations";

    private async Task<Guid> SeedArtifactAsync(
        string name, VisibilityScope visibility = VisibilityScope.PartyVisible, ArtifactType type = ArtifactType.Location)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;
        var artifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = type, Name = name,
            Summary = $"About {name}.", Visibility = visibility, Status = ArtifactStatus.Active,
            CreatedAt = now, UpdatedAt = now
        };
        db.Artifacts.Add(artifact);
        await db.SaveChangesAsync();
        return artifact.Id;
    }

    private Task<Source> SeedSessionAsync(Guid creatorUserId) =>
        SourceTestHelpers.CreateTestSourceAsync(
            _factory, _scenario.World.Id, creatorUserId, type: SourceType.SessionNote,
            processingStatus: SourceProcessingStatus.Processed);

    [Test]
    public async Task Link_ThenList_ReturnsLocation()
    {
        var session = await SeedSessionAsync(_scenario.GmUserId);
        var locationId = await SeedArtifactAsync("Saltmere");

        var post = await _scenario.GmClient.PostAsJsonAsync(Route(session.Id), new LinkLocationRequest(locationId));
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK), await post.Content.ReadAsStringAsync());

        var list = await (await _scenario.GmClient.GetAsync(Route(session.Id)))
            .Content.ReadFromJsonAsync<List<LinkedLocationResponse>>();
        Assert.That(list!, Has.Count.EqualTo(1));
        Assert.That(list![0].ArtifactId, Is.EqualTo(locationId));
        Assert.That(list[0].Name, Is.EqualTo("Saltmere"));
    }

    [Test]
    public async Task Link_NonLocationArtifact_Returns400()
    {
        var session = await SeedSessionAsync(_scenario.GmUserId);
        var characterId = await SeedArtifactAsync("Harbourmaster Vane", type: ArtifactType.Character);

        var post = await _scenario.GmClient.PostAsJsonAsync(Route(session.Id), new LinkLocationRequest(characterId));

        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Link_ByNonCreatorPlayer_Returns403()
    {
        var session = await SeedSessionAsync(_scenario.GmUserId); // GM owns it
        var locationId = await SeedArtifactAsync("Saltmere");

        var post = await _scenario.PlayerClient.PostAsJsonAsync(Route(session.Id), new LinkLocationRequest(locationId));

        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Unlink_RemovesTheLink()
    {
        var session = await SeedSessionAsync(_scenario.GmUserId);
        var locationId = await SeedArtifactAsync("Saltmere");
        await _scenario.GmClient.PostAsJsonAsync(Route(session.Id), new LinkLocationRequest(locationId));

        var del = await _scenario.GmClient.DeleteAsync($"{Route(session.Id)}/{locationId}");
        Assert.That(del.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var list = await (await _scenario.GmClient.GetAsync(Route(session.Id)))
            .Content.ReadFromJsonAsync<List<LinkedLocationResponse>>();
        Assert.That(list!, Is.Empty);
    }

    [Test]
    public async Task List_Player_DoesNotSeeGmOnlyLocation()
    {
        var session = await SeedSessionAsync(_scenario.GmUserId); // PartyVisible, so the player can read it
        var partyId = await SeedArtifactAsync("Black Harbor", VisibilityScope.PartyVisible);
        var secretId = await SeedArtifactAsync("Smuggler's Cove", VisibilityScope.GMOnly);
        await _scenario.GmClient.PostAsJsonAsync(Route(session.Id), new LinkLocationRequest(partyId));
        await _scenario.GmClient.PostAsJsonAsync(Route(session.Id), new LinkLocationRequest(secretId));

        var gm = await (await _scenario.GmClient.GetAsync(Route(session.Id)))
            .Content.ReadFromJsonAsync<List<LinkedLocationResponse>>();
        var player = await (await _scenario.PlayerClient.GetAsync(Route(session.Id)))
            .Content.ReadFromJsonAsync<List<LinkedLocationResponse>>();

        Assert.That(gm!.Select(l => l.Name), Is.EquivalentTo(new[] { "Black Harbor", "Smuggler's Cove" }));
        Assert.That(player!.Select(l => l.Name), Is.EquivalentTo(new[] { "Black Harbor" }));
    }
}
