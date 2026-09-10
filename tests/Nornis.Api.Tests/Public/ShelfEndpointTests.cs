using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Public;

/// <summary>
/// Shelf links end to end: the GM mints one for a player who is not on Nornis, and an
/// anonymous client opens exactly the party shelf with it. The authorization tests are the
/// ones that matter — the anonymous route family is new, and every other route must still
/// refuse strangers.
/// </summary>
[TestFixture]
public class ShelfEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;
    private HttpClient _anonymous = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _anonymous = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _anonymous.Dispose();
        _factory.Dispose();
    }

    private static async Task<Guid> AddHenryAsync(SourceTestScenario scenario)
    {
        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/players", new CreatePlayerRequest("Henry"));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlayerResponse>())!.Id;
    }

    /// <summary>The upload handshake, as the Library page does it: ticket, bytes, confirm.</summary>
    private async Task<Guid> UploadAsync(SourceTestScenario scenario, string title, string visibility)
    {
        var ticket = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/request-upload",
            new RequestLibraryUploadRequest(title, $"{title.Replace(' ', '-')}.pdf", "application/pdf", 2048, "Sourcebook", visibility));
        Assert.That(ticket.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ticket.Content.ReadAsStringAsync());
        var upload = (await ticket.Content.ReadFromJsonAsync<LibraryUploadResponse>())!;

        var path = new Uri(upload.UploadUrl).AbsolutePath.TrimStart('/');
        _factory.BlobStorage.Blobs[path] = (new byte[2048], "application/pdf");

        var confirm = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/library/{upload.Document.Id}/confirm", null);
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK), await confirm.Content.ReadAsStringAsync());
        return upload.Document.Id;
    }

    private static async Task<ShelfLinkResponse> MintAsync(SourceTestScenario scenario, Guid playerId)
    {
        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/shelf-links", new CreateShelfLinkRequest(playerId));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ShelfLinkResponse>())!;
    }

    [Test]
    [Category("Authorization")]
    public async Task Link_OpensThePartyShelfOnly_AndDownloads()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var henry = await AddHenryAsync(scenario);
        var partyGuide = await UploadAsync(scenario, "Players Guide", "PartyVisible");
        var gmBook = await UploadAsync(scenario, "Gamemasters Guide", "GMOnly");
        var link = await MintAsync(scenario, henry);

        var shelf = await _anonymous.GetFromJsonAsync<ShelfResponse>($"/api/shelf/{link.Code}");
        var download = await _anonymous.GetAsync($"/api/shelf/{link.Code}/documents/{partyGuide}/download");
        var gmDownload = await _anonymous.GetAsync($"/api/shelf/{link.Code}/documents/{gmBook}/download");

        Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK), await download.Content.ReadAsStringAsync());
        var file = (await download.Content.ReadFromJsonAsync<LibraryDownloadResponse>())!;
        Assert.Multiple(() =>
        {
            Assert.That(shelf!.PlayerName, Is.EqualTo("Henry"));
            Assert.That(shelf.WorldName, Is.EqualTo(scenario.World.Name));
            Assert.That(shelf.Documents, Has.Count.EqualTo(1), "the party shelf, and only it");
            Assert.That(shelf.Documents[0].Id, Is.EqualTo(partyGuide));
            Assert.That(file.FileName, Is.EqualTo("Players-Guide.pdf"));
            Assert.That(gmDownload.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task Open_StampsLastUsed_VisibleToTheGm()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var henry = await AddHenryAsync(scenario);
        var link = await MintAsync(scenario, henry);
        Assert.That(link.LastUsedAt, Is.Null);

        await _anonymous.GetAsync($"/api/shelf/{link.Code}");

        var listed = await scenario.GmClient.GetFromJsonAsync<List<ShelfLinkResponse>>($"/api/worlds/{scenario.World.Id}/shelf-links");
        Assert.That(listed!.Single(l => l.Id == link.Id).LastUsedAt, Is.Not.Null);
    }

    [Test]
    [Category("Authorization")]
    public async Task Revoked_AndUnknown_AnswerTheSame404()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var henry = await AddHenryAsync(scenario);
        var link = await MintAsync(scenario, henry);

        var revoke = await scenario.GmClient.DeleteAsync($"/api/worlds/{scenario.World.Id}/shelf-links/{link.Id}");
        var revoked = await _anonymous.GetAsync($"/api/shelf/{link.Code}");
        var unknown = await _anonymous.GetAsync("/api/shelf/not-a-code");

        var revokedBody = await revoked.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(revoke.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(revoked.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(revokedBody, Is.EqualTo(unknownBody));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task Mint_AsPlayer_Returns403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var henry = await AddHenryAsync(scenario);

        var response = await scenario.PlayerClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/shelf-links", new CreateShelfLinkRequest(henry));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Mint_ForALinkedPlayer_Returns409()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var mine = await scenario.PlayerClient.GetFromJsonAsync<PlayerResponse>($"/api/worlds/{scenario.World.Id}/players/me");

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/shelf-links", new CreateShelfLinkRequest(mine!.Id));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("player_linked"));
    }

    [Test]
    [Category("Authorization")]
    public async Task Links_AreGmOnly_AndNeverAnonymous()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var anonymous = await _anonymous.GetAsync($"/api/worlds/{scenario.World.Id}/shelf-links");
        var player = await scenario.PlayerClient.GetAsync($"/api/worlds/{scenario.World.Id}/shelf-links");
        var observer = await scenario.ObserverClient.GetAsync($"/api/worlds/{scenario.World.Id}/shelf-links");

        Assert.Multiple(() =>
        {
            Assert.That(anonymous.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(player.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(observer.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        });
    }

    [Test]
    [Category("Authorization")]
    public async Task Shelf_OpensNothingElse()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var henry = await AddHenryAsync(scenario);
        var link = await MintAsync(scenario, henry);

        // The code is not a bearer token for the rest of the API: world-scoped routes still 401.
        var artifacts = await _anonymous.GetAsync($"/api/worlds/{scenario.World.Id}/artifacts?code={link.Code}");
        var library = await _anonymous.GetAsync($"/api/worlds/{scenario.World.Id}/library");

        Assert.That(artifacts.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(library.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }
}
