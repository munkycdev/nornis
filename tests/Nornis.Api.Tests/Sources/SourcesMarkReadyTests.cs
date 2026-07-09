using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

[TestFixture]
public class SourcesMarkReadyTests
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
    public void TearDown()
    {
        _scenario.GmClient.Dispose();
        _scenario.PlayerClient.Dispose();
        _scenario.ObserverClient.Dispose();
        _factory.Dispose();
    }

    private string MarkReadyUrl(Guid sourceId) =>
        $"/api/worlds/{_scenario.World.Id}/sources/{sourceId}/ready";

    #region Happy Path — Mark Ready from Draft

    [Test]
    public async Task MarkReady_FromDraft_ByCreator_SucceedsAndTransitionsToQueued()
    {
        // Arrange — Player creates a Draft source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act
        var response = await _scenario.PlayerClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(source.Id));
        Assert.That(result.ProcessingStatus, Is.EqualTo("Queued"));
        Assert.That(result.Title, Is.EqualTo("Tavrin's Journal — The Silver Key"));
    }

    [Test]
    public async Task MarkReady_FromDraft_ByGm_SucceedsAndTransitionsToQueued()
    {
        // Arrange — Player creates a Draft source, but GM marks it ready
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Session 4 — Questioning Captain Voss",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act — GM can mark any source ready
        var response = await _scenario.GmClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ProcessingStatus, Is.EqualTo("Queued"));
    }

    #endregion

    #region Invalid Status Transitions — Non-Draft Returns 409

    [Test]
    [TestCase(SourceProcessingStatus.Ready)]
    [TestCase(SourceProcessingStatus.Queued)]
    [TestCase(SourceProcessingStatus.Processing)]
    [TestCase(SourceProcessingStatus.Processed)]
    public async Task MarkReady_FromNonDraftStatus_Returns409(SourceProcessingStatus currentStatus)
    {
        // Arrange — Create a source already in a non-Draft state
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: $"Source in {currentStatus} — Black Harbor Clue",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: currentStatus);

        // Act
        var response = await _scenario.GmClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    #endregion

    #region Authorization — Non-Creator Non-GM Cannot Mark Ready

    [Test]
    public async Task MarkReady_ByNonCreatorPlayer_Returns403()
    {
        // Arrange — GM creates a source, Player (non-creator, non-GM) tries to mark ready
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "GM Notes — Black Harbor Conspiracy",
            type: SourceType.GMNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act — Player who is not the creator tries to mark ready
        var response = await _scenario.PlayerClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task MarkReady_ByObserver_Returns403()
    {
        // Arrange — Player creates a source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Session 5 — The Missing Caravan",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Act — Observer tries to mark ready
        var response = await _scenario.ObserverClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region Queue Failure — Source Stays at Ready, Returns 502

    [Test]
    public async Task MarkReady_QueueFailure_LeavesSourceAtReady_Returns502()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 6 — Captain Voss Escapes",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            processingStatus: SourceProcessingStatus.Draft);

        // Configure the fake queue to fail
        _factory.ExtractionQueueClient.ConfigureToFail(true);

        // Act
        var response = await _scenario.GmClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert — Should return 502 Bad Gateway
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadGateway));

        // Reset the queue client and verify source is at Ready (not Queued)
        _factory.ExtractionQueueClient.ConfigureToFail(false);

        var getResponse = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");
        Assert.That(getResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var result = await getResponse.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ProcessingStatus, Is.EqualTo("Ready"));
    }

    #endregion

    #region Extraction Message Verification

    [Test]
    public async Task MarkReady_Success_ExtractionMessageContainsCorrectSourceIdAndWorldId()
    {
        // Arrange
        _factory.ExtractionQueueClient.Reset();

        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Discovery — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private,
            processingStatus: SourceProcessingStatus.Draft);

        // Act
        var response = await _scenario.PlayerClient.PostAsync(MarkReadyUrl(source.Id), null);

        // Assert — Request succeeds
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Assert — Extraction message was sent with correct identifiers
        Assert.That(_factory.ExtractionQueueClient.SentMessages, Has.Count.EqualTo(1));

        var message = _factory.ExtractionQueueClient.SentMessages[0];
        Assert.That(message.SourceId, Is.EqualTo(source.Id));
        Assert.That(message.WorldId, Is.EqualTo(_scenario.World.Id));
    }

    #endregion
}
