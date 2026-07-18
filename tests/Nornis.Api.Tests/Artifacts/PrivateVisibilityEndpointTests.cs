using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Artifacts;

/// <summary>
/// End-to-end proof that one user's Private knowledge does not reach another member
/// through the artifact and canon endpoints. Runs the real EF query predicate (via the
/// InMemory provider) plus controller and auth wiring.
/// </summary>
[TestFixture]
[Category("Feature: content-visibility")]
public class PrivateVisibilityEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private SourceTestScenario _scenario = null!;

    private Artifact _gmsPrivateArtifact = null!;   // owned by the GM user
    private Artifact _playersOwnPrivate = null!;    // owned by the player (Tavrin)
    private ArtifactFact _gmsPrivateFact = null!;   // GM's private fact on a shared artifact
    private Artifact _sharedArtifact = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;

        _sharedArtifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = ArtifactType.Location,
            Name = "Black Harbor", Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        _gmsPrivateArtifact = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = ArtifactType.Character,
            Name = "Kelda Secret Contact", Visibility = VisibilityScope.Private,
            CreatedByUserId = _scenario.GmUserId,
            Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        _playersOwnPrivate = new Artifact
        {
            Id = Guid.NewGuid(), WorldId = _scenario.World.Id, Type = ArtifactType.Character,
            Name = "Tavrin Hidden Ally", Visibility = VisibilityScope.Private,
            CreatedByUserId = _scenario.PlayerUserId,
            Status = ArtifactStatus.Active, CreatedAt = now, UpdatedAt = now
        };
        _gmsPrivateFact = new ArtifactFact
        {
            Id = Guid.NewGuid(), ArtifactId = _sharedArtifact.Id,
            Predicate = "hidden vault", Value = "beneath the lighthouse",
            TruthState = TruthState.Likely, Visibility = VisibilityScope.Private,
            CreatedByUserId = _scenario.GmUserId, CreatedAt = now, UpdatedAt = now
        };

        db.Artifacts.AddRange(_sharedArtifact, _gmsPrivateArtifact, _playersOwnPrivate);
        db.ArtifactFacts.Add(_gmsPrivateFact);
        await db.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown() => _factory.Dispose();

    [Test]
    public async Task List_Player_SeesOwnPrivateButNotOthers()
    {
        var body = await (await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("Tavrin Hidden Ally"), "own Private artifact is listed");
        Assert.That(body, Does.Not.Contain("Kelda Secret Contact"), "another user's Private artifact is not");
        Assert.That(body, Does.Contain("Black Harbor"));
    }

    [Test]
    public async Task List_Gm_SeesAllPrivate()
    {
        var body = await (await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Contain("Kelda Secret Contact"));
        Assert.That(body, Does.Contain("Tavrin Hidden Ally"), "GMs see every member's Private content");
    }

    [Test]
    public async Task Detail_Player_GetsNotFoundForAnotherUsersPrivateArtifact()
    {
        var response = await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts/{_gmsPrivateArtifact.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "not-found, not forbidden — existence must not leak");
    }

    [Test]
    public async Task Detail_SharedArtifact_HidesAnotherUsersPrivateFact()
    {
        var body = await (await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts/{_sharedArtifact.Id}")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain("hidden vault"), "the GM's private fact stays hidden");

        var gmBody = await (await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts/{_sharedArtifact.Id}")).Content.ReadAsStringAsync();
        Assert.That(gmBody, Does.Contain("hidden vault"));
    }

    [Test]
    public async Task Graph_Player_ExcludesAnotherUsersPrivateNode()
    {
        var body = await (await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts/graph")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain("Kelda Secret Contact"));
        Assert.That(body, Does.Contain("Tavrin Hidden Ally"));
    }

    [Test]
    public async Task Canon_Player_ExcludesAnotherUsersPrivateFact()
    {
        var body = await (await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/canon")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain("hidden vault"));
    }

    [Test]
    public async Task Observer_SeesNoPrivateContentAtAll()
    {
        var body = await (await _scenario.ObserverClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/artifacts")).Content.ReadAsStringAsync();

        Assert.That(body, Does.Not.Contain("Kelda Secret Contact"));
        Assert.That(body, Does.Not.Contain("Tavrin Hidden Ally"));
        Assert.That(body, Does.Contain("Black Harbor"));
    }
}
