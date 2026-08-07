using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// The convergence endpoint end-to-end. Its whole content is material the party cannot see, so
/// the role gate is the feature rather than a decoration on it.
/// </summary>
[TestFixture]
[Category("Feature: convergence-gauge")]
public class ConvergenceEndpointTests
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

    private string Url => $"/api/worlds/{_scenario.World.Id}/convergence";

    private async Task<(Guid ArtifactId, Guid FactId)> SeedHiddenFactAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var now = DateTimeOffset.UtcNow;

        var artifact = new Artifact
        {
            Id = Guid.NewGuid(),
            WorldId = _scenario.World.Id,
            Name = "Captain Voss",
            Type = ArtifactType.Character,
            Visibility = VisibilityScope.PartyVisible,
            Status = ArtifactStatus.Active,
            CreatedAt = now.AddDays(-120),
            UpdatedAt = now
        };

        var secret = new ArtifactFact
        {
            Id = Guid.NewGuid(),
            ArtifactId = artifact.Id,
            Predicate = "true allegiance",
            Value = "sworn to the Vespergale cult",
            Visibility = VisibilityScope.GMOnly,
            TruthState = TruthState.Confirmed,
            CreatedAt = now.AddDays(-90),
            UpdatedAt = now
        };

        db.Artifacts.Add(artifact);
        db.ArtifactFacts.Add(secret);
        await db.SaveChangesAsync();

        return (artifact.Id, secret.Id);
    }

    [Test]
    [Category("Authorization")]
    public async Task Get_AsPlayer_Returns403()
    {
        await SeedHiddenFactAsync();

        var response = await _scenario.PlayerClient.GetAsync(Url);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    [Category("Authorization")]
    public async Task Get_AsObserver_Returns403()
    {
        await SeedHiddenFactAsync();

        var response = await _scenario.ObserverClient.GetAsync(Url);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    [Category("Authorization")]
    public async Task Get_NonMember_DoesNotRevealTheWorldExists()
    {
        var strangersWorld = Guid.NewGuid();

        var response = await _scenario.GmClient.GetAsync($"/api/worlds/{strangersWorld}/convergence");

        // 403 rather than 404, and deliberately so: WorldMemberActionFilter answers non-members
        // identically whether or not the world exists, so the status cannot be used to probe for
        // one. The feature's design doc said 404 and was wrong about the house convention.
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Get_AsGm_RanksTheHiddenFact()
    {
        var (artifactId, factId) = await SeedHiddenFactAsync();

        var response = await _scenario.GmClient.GetAsync(Url);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var gauge = await response.Content.ReadFromJsonAsync<ConvergenceResponse>();
        Assert.That(gauge, Is.Not.Null);

        var candidate = gauge!.Candidates.Single(c => c.Id == factId);
        Assert.Multiple(() =>
        {
            Assert.That(candidate.Kind, Is.EqualTo("Fact"));
            Assert.That(candidate.AnchorArtifactId, Is.EqualTo(artifactId));
            Assert.That(candidate.AnchorName, Is.EqualTo("Captain Voss"));
            Assert.That(candidate.Components.DaysHidden, Is.EqualTo(90).Within(1));
            Assert.That(candidate.Components.IsSelfContained, Is.True,
                "the anchor is already party-visible, so the fact reveals on its own");
            Assert.That(candidate.MissingArtifactIds, Is.Empty);
            Assert.That(candidate.Score, Is.GreaterThan(0));
        });
    }

    [Test]
    public async Task Get_WithNoAssessment_ReportsTheContradictionComponentUnavailable()
    {
        await SeedHiddenFactAsync();

        var gauge = await _scenario.GmClient.GetFromJsonAsync<ConvergenceResponse>(Url);

        // Null assessment and an unassessed component, not a zero that reads as "checked".
        Assert.Multiple(() =>
        {
            Assert.That(gauge!.AssessmentId, Is.Null);
            Assert.That(gauge.Candidates.Single().Components.ContradictionAssessed, Is.False);
            Assert.That(gauge.Candidates.Single().Components.ContradictionPressure, Is.Null);
        });
    }

    [Test]
    public async Task Get_WithNothingHidden_Returns200AndAnEmptyGauge()
    {
        var gauge = await _scenario.GmClient.GetFromJsonAsync<ConvergenceResponse>(Url);

        // An empty gauge is a fact about the world, not an error.
        Assert.Multiple(() =>
        {
            Assert.That(gauge!.Candidates, Is.Empty);
            Assert.That(gauge.TotalCandidates, Is.Zero);
            Assert.That(gauge.WorldId, Is.EqualTo(_scenario.World.Id));
        });
    }

    [Test]
    public async Task Get_DoesNotChangeAnything()
    {
        var (_, factId) = await SeedHiddenFactAsync();

        await _scenario.GmClient.GetAsync(Url);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        var fact = db.ArtifactFacts.Single(f => f.Id == factId);

        Assert.That(fact.Visibility, Is.EqualTo(VisibilityScope.GMOnly),
            "reading the gauge must never be a way to reveal something");
    }
}
