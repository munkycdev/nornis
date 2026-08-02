using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Worlds;

/// <summary>
/// End-to-end coverage for POST /api/worlds/{worldId}/export: GM-only, packages the
/// selected categories into a zip in blob storage, and returns a SAS download URL.
/// </summary>
[TestFixture]
public class WorldExportTests
{
    private const string WorldName = "Black Harbor Investigation";

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

    private async Task<Guid> CreateWorldAsGm(HttpClient gmClient)
    {
        var response = await gmClient.PostAsJsonAsync("/api/worlds",
            new CreateWorldRequest(WorldName, "A dark mystery", "D&D 5e"));
        response.EnsureSuccessStatusCode();

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        return world!.Id;
    }

    private static Task<HttpResponseMessage> PostExport(HttpClient client, Guid worldId, params string[] categories) =>
        client.PostAsJsonAsync($"/api/worlds/{worldId}/export", new ExportWorldRequest(categories));

    private static JsonDocument ReadJsonEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        Assert.That(entry, Is.Not.Null, $"zip entry {entryName} missing");
        using var stream = entry!.Open();
        return JsonDocument.Parse(stream);
    }

    [Test]
    public async Task ExportWorld_AsGm_UploadsZipAndReturnsDownloadUrl()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-exp1", email: "voss@blackharbor.com", nickname: "Captain Voss");
        var worldId = await CreateWorldAsGm(gmClient);

        var campaign = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/campaigns",
            new CreateCampaignRequest("Season One"));
        campaign.EnsureSuccessStatusCode();

        var source = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/sources",
            new CreateSourceRequest("Session 1", "SessionNote", "PartyVisible", Body: "We sailed."));
        source.EnsureSuccessStatusCode();

        var response = await PostExport(gmClient, worldId, "Members", "Campaigns", "Sources");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var export = await response.Content.ReadFromJsonAsync<ExportWorldResponse>();
        Assert.That(export!.FileName, Does.EndWith(".zip"));
        Assert.That(export.SizeBytes, Is.GreaterThan(0));

        var blobPath = $"worlds/{worldId}/exports/{export.FileName}";
        Assert.That(export.DownloadUrl, Is.EqualTo($"https://blob.test/{blobPath}?sas=download"));
        Assert.That(_factory.BlobStorage.Blobs.ContainsKey(blobPath), Is.True);
        Assert.That(_factory.BlobStorage.Blobs[blobPath].ContentType, Is.EqualTo("application/zip"));

        using var zip = new ZipArchive(
            new MemoryStream(_factory.BlobStorage.Blobs[blobPath].Content), ZipArchiveMode.Read);

        using (var world = ReadJsonEntry(zip, "world.json"))
        {
            Assert.That(world.RootElement.GetProperty("name").GetString(), Is.EqualTo(WorldName));
        }

        using (var members = ReadJsonEntry(zip, "members.json"))
        {
            var member = members.RootElement.EnumerateArray().Single();
            Assert.That(member.GetProperty("role").GetString(), Is.EqualTo("GM"));
        }

        using (var campaigns = ReadJsonEntry(zip, "campaigns.json"))
        {
            var item = campaigns.RootElement.GetProperty("campaigns").EnumerateArray().Single();
            Assert.That(item.GetProperty("name").GetString(), Is.EqualTo("Season One"));
        }

        using (var sources = ReadJsonEntry(zip, "sources.json"))
        {
            var item = sources.RootElement.GetProperty("sources").EnumerateArray().Single();
            Assert.That(item.GetProperty("title").GetString(), Is.EqualTo("Session 1"));
            Assert.That(item.GetProperty("body").GetString(), Is.EqualTo("We sailed."));
        }

        using (var manifest = ReadJsonEntry(zip, "manifest.json"))
        {
            Assert.That(manifest.RootElement.GetProperty("formatVersion").GetInt32(), Is.EqualTo(1));
            var categories = manifest.RootElement.GetProperty("categories").EnumerateArray()
                .Select(c => c.GetString()).ToList();
            Assert.That(categories, Is.EquivalentTo(new[] { "Members", "Campaigns", "Sources" }));
        }

        // Unselected categories stay out of the zip.
        Assert.That(zip.GetEntry("codex.json"), Is.Null);
        Assert.That(zip.GetEntry("library.json"), Is.Null);
    }

    [Test]
    public async Task ExportWorld_InvalidCategory_Returns400()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-exp2", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var response = await PostExport(gmClient, worldId, "Sources", "Bogus");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("invalid_category"));
    }

    [Test]
    public async Task ExportWorld_NoCategories_Returns400()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-exp3", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var response = await PostExport(gmClient, worldId);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("no_categories"));
    }

    [Test]

    [Category("Authorization")]
    public async Task ExportWorld_AsPlayer_Returns403()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-exp4", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var playerClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|player-tavrin-exp4", email: "tavrin@example.com", nickname: "Tavrin");
        await playerClient.GetAsync("/api/worlds"); // provision the user

        Guid playerUserId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            playerUserId = (await db.Users.FirstAsync(u => u.Auth0SubjectId == "auth0|player-tavrin-exp4")).Id;
        }

        var add = await gmClient.PostAsJsonAsync($"/api/worlds/{worldId}/members",
            new AddWorldMemberRequest(playerUserId, "Player"));
        add.EnsureSuccessStatusCode();

        var response = await PostExport(playerClient, worldId, "Sources");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.BlobStorage.Blobs.Keys, Has.None.Contain("/exports/"));
    }

    [Test]

    [Category("Authorization")]
    public async Task ExportWorld_AsNonMember_Returns403()
    {
        var gmClient = _factory.CreateAuthenticatedClient(
            sub: "auth0|gm-voss-exp5", email: "voss@blackharbor.com");
        var worldId = await CreateWorldAsGm(gmClient);

        var outsider = _factory.CreateAuthenticatedClient(
            sub: "auth0|outsider-exp5", email: "outsider@example.com");

        var response = await PostExport(outsider, worldId, "Sources");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
