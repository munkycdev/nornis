using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

[TestFixture]
public class SourcesControllerDeleteTests
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
    public async Task Delete_ByCreator_Returns204_AndSourceNoLongerExists()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act
        var deleteResponse = await scenario.PlayerClient.DeleteAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");

        // Assert
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify the source no longer exists (GET should return 404)
        var getResponse = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_ByGm_Returns204_ForAnySource()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        // Player creates a source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.PlayerUserId,
            title: "Session 4 — Questioning Captain Voss",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act — GM deletes the player's source
        var deleteResponse = await scenario.GmClient.DeleteAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");

        // Assert
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify deletion
        var getResponse = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_ByNonCreatorPlayer_Returns403()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        // GM creates a source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "GM Notes — Black Harbor Conspiracy",
            type: SourceType.GMNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act — Player (non-creator, non-GM) tries to delete
        var deleteResponse = await scenario.PlayerClient.DeleteAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");

        // Assert
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    [TestCase(SourceProcessingStatus.Queued)]
    [TestCase(SourceProcessingStatus.Processing)]
    public async Task Delete_WhenQueuedOrProcessing_Returns409(SourceProcessingStatus blockedStatus)
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: "Session 5 — The Missing Caravan",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: blockedStatus);

        // Act — Even the GM cannot delete while processing
        var deleteResponse = await scenario.GmClient.DeleteAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");

        // Assert
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    [TestCase(SourceProcessingStatus.Draft)]
    [TestCase(SourceProcessingStatus.Ready)]
    [TestCase(SourceProcessingStatus.Processed)]
    [TestCase(SourceProcessingStatus.Failed)]
    public async Task Delete_WhenDraftReadyProcessedOrFailed_Returns204(SourceProcessingStatus allowedStatus)
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            scenario.Campaign.Id,
            scenario.GmUserId,
            title: $"Source in {allowedStatus} status — Silver Key clue",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: allowedStatus);

        // Act
        var deleteResponse = await scenario.GmClient.DeleteAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");

        // Assert
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Verify deleted
        var getResponse = await scenario.GmClient.GetAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{source.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Delete_NonExistentSource_Returns404()
    {
        // Arrange
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var nonExistentSourceId = Guid.NewGuid();

        // Act
        var deleteResponse = await scenario.GmClient.DeleteAsync(
            $"/api/campaigns/{scenario.Campaign.Id}/sources/{nonExistentSourceId}");

        // Assert
        Assert.That(deleteResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
