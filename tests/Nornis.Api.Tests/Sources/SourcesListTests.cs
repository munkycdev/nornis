using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

[TestFixture]
public class SourcesListTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
    }

    [Test]
    public async Task ListSources_AsGm_ReturnsAllSourcesRegardlessOfVisibility()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        // Create sources with all visibility types
        var partyVisible = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.PlayerUserId,
            title: "Session 4 — Questioning Captain Voss",
            visibility: VisibilityScope.PartyVisible);

        var privateSource = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private);

        var gmOnly = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "GM Notes — Captain Voss's True Allegiance",
            type: SourceType.GMNote,
            visibility: VisibilityScope.GMOnly);

        // Act
        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var sources = await response.Content.ReadFromJsonAsync<List<SourceListItemResponse>>();
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources!.Count, Is.EqualTo(3));

        var sourceIds = sources.Select(s => s.Id).ToList();
        Assert.That(sourceIds, Contains.Item(partyVisible.Id));
        Assert.That(sourceIds, Contains.Item(privateSource.Id));
        Assert.That(sourceIds, Contains.Item(gmOnly.Id));
    }

    [Test]
    public async Task ListSources_AsPlayer_ReturnsPartyVisibleAndOwnPrivateSources()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var partyVisible = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "Session 4 — Questioning Captain Voss",
            visibility: VisibilityScope.PartyVisible);

        var playerPrivate = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private);

        // Another user's private source — player should NOT see this
        var otherPrivate = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "GM's Private Prep Notes",
            type: SourceType.GMNote,
            visibility: VisibilityScope.Private);

        var gmOnly = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "GM Only — Black Harbor Secret",
            type: SourceType.GMNote,
            visibility: VisibilityScope.GMOnly);

        // Act
        var response = await scenario.PlayerClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var sources = await response.Content.ReadFromJsonAsync<List<SourceListItemResponse>>();
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources!.Count, Is.EqualTo(2));

        var sourceIds = sources.Select(s => s.Id).ToList();
        Assert.That(sourceIds, Contains.Item(partyVisible.Id));
        Assert.That(sourceIds, Contains.Item(playerPrivate.Id));
        Assert.That(sourceIds, Does.Not.Contain(otherPrivate.Id));
        Assert.That(sourceIds, Does.Not.Contain(gmOnly.Id));
    }

    [Test]
    public async Task ListSources_AsObserver_ReturnsOnlyPartyVisibleSources()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var partyVisible = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "Session 4 — Questioning Captain Voss",
            visibility: VisibilityScope.PartyVisible);

        var privateSource = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private);

        var gmOnly = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "GM Notes — Captain Voss's True Allegiance",
            type: SourceType.GMNote,
            visibility: VisibilityScope.GMOnly);

        // Act
        var response = await scenario.ObserverClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var sources = await response.Content.ReadFromJsonAsync<List<SourceListItemResponse>>();
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources!.Count, Is.EqualTo(1));
        Assert.That(sources[0].Id, Is.EqualTo(partyVisible.Id));
    }

    [Test]
    public async Task ListSources_ReturnsOrderedByCreatedAtDescending()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var oldest = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "Session 1 — Arrival at Black Harbor",
            createdAt: DateTimeOffset.UtcNow.AddDays(-3));

        var middle = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "Session 2 — The Missing Caravan",
            createdAt: DateTimeOffset.UtcNow.AddDays(-2));

        var newest = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "Session 3 — The Silver Key Discovery",
            createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        // Act
        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var sources = await response.Content.ReadFromJsonAsync<List<SourceListItemResponse>>();
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources!.Count, Is.EqualTo(3));

        // Verify descending order (newest first)
        Assert.That(sources[0].Id, Is.EqualTo(newest.Id));
        Assert.That(sources[1].Id, Is.EqualTo(middle.Id));
        Assert.That(sources[2].Id, Is.EqualTo(oldest.Id));

        // Double-check CreatedAt values are in descending order
        Assert.That(sources[0].CreatedAt, Is.GreaterThan(sources[1].CreatedAt));
        Assert.That(sources[1].CreatedAt, Is.GreaterThan(sources[2].CreatedAt));
    }

    [Test]
    public async Task ListSources_EmptyCampaign_ReturnsEmptyList()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        // No sources created — campaign is empty

        // Act
        var response = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var sources = await response.Content.ReadFromJsonAsync<List<SourceListItemResponse>>();
        Assert.That(sources, Is.Not.Null);
        Assert.That(sources!, Is.Empty);
    }
}
