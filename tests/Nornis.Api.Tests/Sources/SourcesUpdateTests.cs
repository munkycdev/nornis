using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

[TestFixture]
public class SourcesUpdateTests
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
        _factory.Dispose();
    }

    #region Creator can update own source

    [Test]
    public async Task UpdateSource_CreatorUpdatesOwnSource_ReturnsOkWithUpdatedFields()
    {
        // Arrange — Player creates a source, then updates it
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.PartyVisible,
            body: "Found the key in Voss's quarters.",
            uri: "https://example.com/notes");

        var updateRequest = new UpdateSourceRequest(
            Title: "Tavrin's Journal — The Silver Key (Revised)",
            Body: "Found the Silver Key hidden beneath the floorboards in Voss's quarters.");

        // Act
        var response = await _scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Title, Is.EqualTo("Tavrin's Journal — The Silver Key (Revised)"));
        Assert.That(updated.Body, Is.EqualTo("Found the Silver Key hidden beneath the floorboards in Voss's quarters."));
        Assert.That(updated.Id, Is.EqualTo(source.Id));
        Assert.That(updated.WorldId, Is.EqualTo(_scenario.World.Id));
        Assert.That(updated.CreatedByUserId, Is.EqualTo(_scenario.PlayerUserId));
    }

    #endregion

    #region GM can update any source

    [Test]
    public async Task UpdateSource_GmUpdatesPlayerSource_ReturnsOkWithUpdatedFields()
    {
        // Arrange — Player's source, GM updates it
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Session 4 — Questioning Captain Voss",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            body: "We confronted Voss at the docks.");

        var updateRequest = new UpdateSourceRequest(
            Title: "Session 4 — The Interrogation of Captain Voss",
            Visibility: "GMOnly");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(updated, Is.Not.Null);
        Assert.That(updated!.Title, Is.EqualTo("Session 4 — The Interrogation of Captain Voss"));
        Assert.That(updated.Visibility, Is.EqualTo("GMOnly"));
        Assert.That(updated.Body, Is.EqualTo("We confronted Voss at the docks."));
    }

    #endregion

    #region Non-creator Player cannot update

    [Test]
    public async Task UpdateSource_NonCreatorPlayerUpdates_Returns403()
    {
        // Arrange — GM creates a PartyVisible source; a different Player tries to update
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "GM Notes — Black Harbor Conspirators",
            type: SourceType.GMNote,
            visibility: VisibilityScope.PartyVisible);

        var updateRequest = new UpdateSourceRequest(Title: "Hijacked Title");

        // Act
        var response = await _scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region Observer cannot update

    [Test]
    public async Task UpdateSource_ObserverUpdates_Returns403()
    {
        // Arrange — GM creates a PartyVisible source; Observer tries to update
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 3 — The Missing Caravan",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible);

        var updateRequest = new UpdateSourceRequest(Title: "Observer's Edit");

        // Act
        var response = await _scenario.ObserverClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    #endregion

    #region Invalid title returns 400

    [Test]
    public async Task UpdateSource_WithEmptyTitle_Returns400()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 5 — Stormwatch");

        var updateRequest = new UpdateSourceRequest(Title: "");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UpdateSource_WithWhitespaceOnlyTitle_Returns400()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 5 — Stormwatch");

        var updateRequest = new UpdateSourceRequest(Title: "   ");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task UpdateSource_WithTitleExceeding200Chars_Returns400()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 5 — Stormwatch");

        var longTitle = new string('A', 201);
        var updateRequest = new UpdateSourceRequest(Title: longTitle);

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion

    #region Player setting GMOnly visibility returns 400

    [Test]
    public async Task UpdateSource_PlayerSetsGMOnlyVisibility_Returns400()
    {
        // Arrange — Player creates their own source, then tries to set it to GMOnly
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Private Notes",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private);

        var updateRequest = new UpdateSourceRequest(Visibility: "GMOnly");

        // Act
        var response = await _scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion

    #region Update blocked when Queued/Processing/Processed returns 409

    [Test]
    public async Task UpdateSource_WhenQueued_Returns409()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Queued Source — Harbor Witnesses",
            processingStatus: SourceProcessingStatus.Queued);

        var updateRequest = new UpdateSourceRequest(Title: "Attempted Edit While Queued");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task UpdateSource_WhenProcessing_Returns409()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Processing Source — Dockside Logs",
            processingStatus: SourceProcessingStatus.Processing);

        var updateRequest = new UpdateSourceRequest(Title: "Attempted Edit While Processing");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task UpdateSource_WhenProcessed_Returns409()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Processed Source — Captain Voss Testimony",
            processingStatus: SourceProcessingStatus.Processed);

        var updateRequest = new UpdateSourceRequest(Title: "Attempted Edit After Processed");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    #endregion

    #region Partial update modifies only specified fields

    [Test]
    public async Task UpdateSource_PartialUpdate_OnlyModifiesSpecifiedFields()
    {
        // Arrange — Create a source with all fields populated
        var originalOccurredAt = new DateTimeOffset(2024, 3, 15, 19, 0, 0, TimeSpan.Zero);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 6 — The Warehouse Raid",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            body: "We raided the warehouse by the eastern docks.",
            uri: "https://example.com/session6",
            occurredAt: originalOccurredAt);

        // Only update the title — all other fields should remain unchanged
        var updateRequest = new UpdateSourceRequest(Title: "Session 6 — The Eastern Dock Raid");

        // Act
        var response = await _scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(updated, Is.Not.Null);

        // Title changed
        Assert.That(updated!.Title, Is.EqualTo("Session 6 — The Eastern Dock Raid"));

        // All other fields remain unchanged
        Assert.That(updated.Body, Is.EqualTo("We raided the warehouse by the eastern docks."));
        Assert.That(updated.Uri, Is.EqualTo("https://example.com/session6"));
        Assert.That(updated.OccurredAt, Is.EqualTo(originalOccurredAt));
        Assert.That(updated.Type, Is.EqualTo("SessionNote"));
        Assert.That(updated.Visibility, Is.EqualTo("PartyVisible"));
        Assert.That(updated.ProcessingStatus, Is.EqualTo("Draft"));
        Assert.That(updated.CreatedByUserId, Is.EqualTo(_scenario.GmUserId));
    }

    [Test]
    public async Task UpdateSource_PartialUpdate_OnlyBodyChanged_OtherFieldsPreserved()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Journal — Suspicious Cargo",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private,
            body: "Saw crates marked with the Silver Key symbol.");

        // Only update body
        var updateRequest = new UpdateSourceRequest(
            Body: "Saw crates marked with the Silver Key symbol. They were being loaded onto the Night Heron.");

        // Act
        var response = await _scenario.PlayerClient.PutAsJsonAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}", updateRequest);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var updated = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(updated, Is.Not.Null);

        // Body changed
        Assert.That(updated!.Body, Is.EqualTo(
            "Saw crates marked with the Silver Key symbol. They were being loaded onto the Night Heron."));

        // Other fields unchanged
        Assert.That(updated.Title, Is.EqualTo("Tavrin's Journal — Suspicious Cargo"));
        Assert.That(updated.Type, Is.EqualTo("JournalEntry"));
        Assert.That(updated.Visibility, Is.EqualTo("Private"));
    }

    #endregion
}
