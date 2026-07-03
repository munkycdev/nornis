using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Campaigns;

[TestFixture]
public class SourcesControllerCreateTests
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

    private string SourcesUrl => $"/api/campaigns/{_scenario.Campaign.Id}/sources";

    #region Valid Creation

    [Test]
    public async Task Create_ByGm_Returns201_WithCorrectSourceResponseFields()
    {
        // Arrange
        var occurredAt = new DateTimeOffset(2024, 3, 15, 19, 0, 0, TimeSpan.Zero);
        var request = new CreateSourceRequest(
            Title: "Session 4 — Questioning Captain Voss",
            Type: "SessionNote",
            Visibility: "PartyVisible",
            Body: "We questioned Captain Voss in Black Harbor. He denied knowing about the missing caravan.",
            Uri: "https://notes.blackharbor.com/session-4",
            OccurredAt: occurredAt);

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var source = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(source, Is.Not.Null);
        Assert.That(source!.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(source.CampaignId, Is.EqualTo(_scenario.Campaign.Id));
        Assert.That(source.Type, Is.EqualTo("SessionNote"));
        Assert.That(source.Title, Is.EqualTo("Session 4 — Questioning Captain Voss"));
        Assert.That(source.Body, Is.EqualTo("We questioned Captain Voss in Black Harbor. He denied knowing about the missing caravan."));
        Assert.That(source.Uri, Is.EqualTo("https://notes.blackharbor.com/session-4"));
        Assert.That(source.OccurredAt, Is.EqualTo(occurredAt));
        Assert.That(source.CreatedAt, Is.Not.EqualTo(default(DateTimeOffset)));
        Assert.That(source.CreatedByUserId, Is.EqualTo(_scenario.GmUserId));
        Assert.That(source.Visibility, Is.EqualTo("PartyVisible"));
        Assert.That(source.ProcessingStatus, Is.EqualTo("Draft"));
    }

    [Test]
    public async Task Create_ByPlayer_Returns201()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "Tavrin's Journal — The Silver Key",
            Type: "JournalEntry",
            Visibility: "Private",
            Body: "I found the Silver Key hidden in Voss's quarters. What does it unlock?");

        // Act
        var response = await _scenario.PlayerClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var source = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(source, Is.Not.Null);
        Assert.That(source!.Id, Is.Not.EqualTo(Guid.Empty));
        Assert.That(source.Title, Is.EqualTo("Tavrin's Journal — The Silver Key"));
        Assert.That(source.Type, Is.EqualTo("JournalEntry"));
        Assert.That(source.Visibility, Is.EqualTo("Private"));
        Assert.That(source.CreatedByUserId, Is.EqualTo(_scenario.PlayerUserId));
        Assert.That(source.ProcessingStatus, Is.EqualTo("Draft"));
    }

    [Test]
    public async Task Create_ProcessingStatus_IsDraftOnCreation()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "GM Notes — Black Harbor Factions",
            Type: "GMNote",
            Visibility: "GMOnly");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var source = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(source, Is.Not.Null);
        Assert.That(source!.ProcessingStatus, Is.EqualTo("Draft"));
    }

    #endregion

    #region Authorization

    [Test]
    public async Task Create_ByObserver_Returns403()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "Observer should not create sources",
            Type: "SessionNote",
            Visibility: "PartyVisible");

        // Act
        var response = await _scenario.ObserverClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Create_PlayerWithGMOnlyVisibility_Returns400()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "Tavrin's Secret Intel",
            Type: "SessionNote",
            Visibility: "GMOnly");

        // Act
        var response = await _scenario.PlayerClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion

    #region Validation — Invalid Enum Strings

    [Test]
    public async Task Create_InvalidSourceType_Returns400()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "Session 5 — The Missing Caravan",
            Type: "InvalidType",
            Visibility: "PartyVisible");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo("invalid_source_type"));
    }

    [Test]
    public async Task Create_InvalidVisibilityScope_Returns400()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "Session 5 — The Missing Caravan",
            Type: "SessionNote",
            Visibility: "WorldVisible");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error, Is.Not.Null);
        Assert.That(error!.Code, Is.EqualTo("invalid_visibility"));
    }

    #endregion

    #region Validation — Title

    [Test]
    public async Task Create_EmptyTitle_Returns400()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "",
            Type: "SessionNote",
            Visibility: "PartyVisible");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Create_WhitespaceTitle_Returns400()
    {
        // Arrange
        var request = new CreateSourceRequest(
            Title: "   ",
            Type: "SessionNote",
            Visibility: "PartyVisible");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Create_TitleExceeding200Chars_Returns400()
    {
        // Arrange
        var longTitle = new string('A', 201);
        var request = new CreateSourceRequest(
            Title: longTitle,
            Type: "SessionNote",
            Visibility: "PartyVisible");

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion

    #region Validation — Body and Uri Length

    [Test]
    public async Task Create_BodyExceeding100000Chars_Returns400()
    {
        // Arrange
        var longBody = new string('X', 100_001);
        var request = new CreateSourceRequest(
            Title: "Session with very long notes",
            Type: "SessionNote",
            Visibility: "PartyVisible",
            Body: longBody);

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task Create_UriExceeding2048Chars_Returns400()
    {
        // Arrange
        var longUri = "https://example.com/" + new string('a', 2_030);
        var request = new CreateSourceRequest(
            Title: "Session with very long URI",
            Type: "WebLink",
            Visibility: "PartyVisible",
            Uri: longUri);

        // Act
        var response = await _scenario.GmClient.PostAsJsonAsync(SourcesUrl, request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    #endregion
}
