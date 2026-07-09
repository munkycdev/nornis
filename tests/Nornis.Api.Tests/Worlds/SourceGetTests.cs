using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// Integration tests for GET /api/worlds/{worldId}/sources/{sourceId}
/// covering visibility enforcement, role-based access, and authorization.
/// Validates: Requirements 2.1–2.7, 9.1–9.4, 10.1, 10.2
/// </summary>
[TestFixture]
public class SourceGetTests
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

    /// <summary>
    /// A GM can retrieve any source regardless of visibility scope.
    /// Validates: Requirements 2.1, 2.7
    /// </summary>
    [Test]
    public async Task GmCanGetPrivateSourceCreatedByAnotherUser()
    {
        // Arrange — Player creates a Private source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Journal — The Silver Key",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private,
            body: "Found a silver key hidden in Voss's quarters.");

        // Act — GM retrieves the Private source
        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(source.Id));
        Assert.That(result.Title, Is.EqualTo("Tavrin's Journal — The Silver Key"));
        Assert.That(result.Visibility, Is.EqualTo("Private"));
    }

    /// <summary>
    /// A GM can retrieve a GMOnly source.
    /// Validates: Requirements 2.1, 2.7
    /// </summary>
    [Test]
    public async Task GmCanGetGmOnlySource()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "GM Notes — Captain Voss's True Allegiance",
            type: SourceType.GMNote,
            visibility: VisibilityScope.GMOnly,
            body: "Voss is secretly working for the Silver Hand faction.");

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(source.Id));
        Assert.That(result.Type, Is.EqualTo("GMNote"));
        Assert.That(result.Visibility, Is.EqualTo("GMOnly"));
    }

    /// <summary>
    /// A Player can retrieve a PartyVisible source.
    /// Validates: Requirements 2.1, 2.4, 9.3
    /// </summary>
    [Test]
    public async Task PlayerCanGetPartyVisibleSource()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 4 — Questioning Captain Voss",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            body: "We questioned Captain Voss in Black Harbor.");

        // Act
        var response = await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(source.Id));
        Assert.That(result.Title, Is.EqualTo("Session 4 — Questioning Captain Voss"));
        Assert.That(result.Visibility, Is.EqualTo("PartyVisible"));
        Assert.That(result.ProcessingStatus, Is.EqualTo("Draft"));
    }

    /// <summary>
    /// A Player can retrieve their own Private source.
    /// Validates: Requirements 2.1, 9.1
    /// </summary>
    [Test]
    public async Task PlayerCanGetOwnPrivateSource()
    {
        // Arrange — Player creates a Private source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Private Notes — Missing Caravan Suspicions",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private,
            body: "I suspect Voss knows more than he admits about the caravan.");

        // Act
        var response = await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(source.Id));
        Assert.That(result.CreatedByUserId, Is.EqualTo(_scenario.PlayerUserId));
        Assert.That(result.Visibility, Is.EqualTo("Private"));
    }

    /// <summary>
    /// A Player cannot retrieve another user's Private source — returns 404 (not 403).
    /// Validates: Requirements 2.2, 9.1, 9.4
    /// </summary>
    [Test]
    public async Task PlayerCannotGetOtherUsersPrivateSource_Returns404()
    {
        // Arrange — GM creates a Private source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Kelda's Secret Research — Silver Key Origins",
            type: SourceType.GMNote,
            visibility: VisibilityScope.Private,
            body: "The Silver Key predates Black Harbor by centuries.");

        // Act — Player attempts to retrieve the GM's Private source
        var response = await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert — returns 404 to avoid leaking existence
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// A Player cannot retrieve a GMOnly source — returns 404 (not 403).
    /// Validates: Requirements 2.3, 9.2, 9.4
    /// </summary>
    [Test]
    public async Task PlayerCannotGetGmOnlySource_Returns404()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "GM Plans — Faction War Escalation",
            type: SourceType.GMNote,
            visibility: VisibilityScope.GMOnly,
            body: "The Silver Hand will attack Black Harbor next session.");

        // Act
        var response = await _scenario.PlayerClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// An Observer can retrieve a PartyVisible source.
    /// Validates: Requirements 2.4, 9.3
    /// </summary>
    [Test]
    public async Task ObserverCanGetPartyVisibleSource()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 3 — Arrival at Black Harbor",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible,
            body: "The party arrived at Black Harbor under cover of night.");

        // Act
        var response = await _scenario.ObserverClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<SourceResponse>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Id, Is.EqualTo(source.Id));
        Assert.That(result.Visibility, Is.EqualTo("PartyVisible"));
    }

    /// <summary>
    /// An Observer cannot retrieve a Private source — returns 404.
    /// Validates: Requirements 2.2, 9.1, 9.4
    /// </summary>
    [Test]
    public async Task ObserverCannotGetPrivateSource_Returns404()
    {
        // Arrange — Player creates a Private source
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.PlayerUserId,
            title: "Tavrin's Private Thoughts — The Missing Caravan",
            type: SourceType.JournalEntry,
            visibility: VisibilityScope.Private,
            body: "Something doesn't add up about the caravan disappearance.");

        // Act
        var response = await _scenario.ObserverClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// An Observer cannot retrieve a GMOnly source — returns 404.
    /// Validates: Requirements 2.3, 9.2, 9.4
    /// </summary>
    [Test]
    public async Task ObserverCannotGetGmOnlySource_Returns404()
    {
        // Arrange
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "GM Secret — True Identity of the Harbor Master",
            type: SourceType.GMNote,
            visibility: VisibilityScope.GMOnly,
            body: "The Harbor Master is an agent of the Weave Syndicate.");

        // Act
        var response = await _scenario.ObserverClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// Requesting a non-existent source returns 404.
    /// Validates: Requirements 2.5
    /// </summary>
    [Test]
    public async Task NonExistentSource_Returns404()
    {
        // Arrange
        var nonExistentSourceId = Guid.NewGuid();

        // Act
        var response = await _scenario.GmClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{nonExistentSourceId}");

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    /// <summary>
    /// A non-member of the world cannot access source endpoints — returns 403.
    /// Validates: Requirements 2.6, 10.1, 10.2
    /// </summary>
    [Test]
    public async Task NonMember_CannotGetSource_Returns403()
    {
        // Arrange — Create a source in the world
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory,
            _scenario.World.Id,
            _scenario.GmUserId,
            title: "Session 5 — The Silver Key Unlocks Something",
            type: SourceType.SessionNote,
            visibility: VisibilityScope.PartyVisible);

        // Create a separate user who is NOT a member of this world
        var nonMemberClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-maren",
            email: "maren@wanderer.com",
            nickname: "Maren");

        // Trigger user provisioning for the non-member
        await nonMemberClient.GetAsync("/api/worlds");

        // Act — Non-member attempts to retrieve the source
        var response = await nonMemberClient.GetAsync(
            $"/api/worlds/{_scenario.World.Id}/sources/{source.Id}");

        // Assert — 403 Forbidden because user is not a world member
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
