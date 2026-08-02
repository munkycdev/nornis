using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Controllers;

/// <summary>
/// POST /api/worlds/demo — demo world instantiation from a template package (feature 20
/// phase B): snapshot copy with fresh ids, creator as sole GM, generated name, rate limit,
/// and the public-access kill switch for demo worlds.
/// </summary>
[TestFixture]
public class DemoWorldTests
{
    private NornisWebApplicationFactory _factory = null!;
    private string _templatePath = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new NornisWebApplicationFactory();
        _templatePath = Path.Combine(Path.GetTempPath(), $"nornis-test-template-{Guid.NewGuid():N}.zip");
        WriteTemplateZip(_templatePath);
    }

    [TearDown]
    public void TearDown()
    {
        _factory.Dispose();
        try
        {
            File.Delete(_templatePath);
        }
        catch (IOException)
        {
        }
    }

    private HttpClient CreateClientWithTemplate(params (string Key, string Value)[] extraSettings)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DemoWorlds:TemplatePath", _templatePath);
            foreach (var (key, value) in extraSettings)
            {
                builder.UseSetting(key, value);
            }
        });

        var token = TestJwtIssuer.GenerateToken("auth0|demo-creator", "demo@vespergale.com", "Demo Dave");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Test]
    public async Task CreateDemo_InstantiatesSnapshotCopy_WithCreatorAsGm()
    {
        var client = CreateClientWithTemplate();

        var response = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: true));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world, Is.Not.Null);
        Assert.That(world!.Name, Is.Not.Empty, "a name must be generated even without AI");
        Assert.That(world.IsDemo, Is.True);
        Assert.That(world.TutorialEnabled, Is.True);
        Assert.That(world.MyRole, Is.EqualTo("GM"));
        Assert.That(world.PublicAccessEnabled, Is.False, "demo worlds start private");

        // The snapshot content came across: both sources and both artifacts, as GM.
        var sources = await client.GetFromJsonAsync<JsonElement>($"/api/worlds/{world.Id}/sources");
        Assert.That(sources.GetArrayLength(), Is.EqualTo(2), "sessions incl. the GM-only prep note");

        var artifacts = await client.GetFromJsonAsync<JsonElement>($"/api/worlds/{world.Id}/artifacts");
        Assert.That(artifacts.GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task CreateDemo_WithoutTutorial_RecordsTheChoice()
    {
        var client = CreateClientWithTemplate();

        var response = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: false));

        var world = await response.Content.ReadFromJsonAsync<WorldResponse>();
        Assert.That(world!.IsDemo, Is.True);
        Assert.That(world.TutorialEnabled, Is.False);
    }

    [Test]
    public async Task CreateDemo_SecondWithinADay_IsRateLimited()
    {
        var client = CreateClientWithTemplate();

        var first = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: false));
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.Created));

        var second = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: false));
        Assert.That(second.StatusCode, Is.EqualTo((HttpStatusCode)429));
    }

    [Test]
    public async Task CreateDemo_WithoutConfiguredTemplate_Returns503()
    {
        // The real template now ships in appsettings + build output, so "unconfigured"
        // must be forced explicitly rather than assumed.
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("DemoWorlds:TemplatePath", ""));
        var token = TestJwtIssuer.GenerateToken("auth0|no-template", "n@t.com", "NoTemplate");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: true));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task CreateDemo_TemplateWithDanglingReference_FailsCleanly()
    {
        // A map pin pointing at an artifact that is not in the package must fail the
        // import (500), not silently orphan the pin.
        WriteTemplateZip(_templatePath, danglingPin: true);
        var client = CreateClientWithTemplate();

        var response = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: true));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));

        // And nothing was half-created.
        var worlds = await client.GetFromJsonAsync<JsonElement>("/api/worlds");
        Assert.That(worlds.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]

    [Category("Authorization")]
    public async Task KillSwitch_BlocksEnablingPublicAccessOnDemoWorlds()
    {
        var client = CreateClientWithTemplate(("DemoWorlds:PublicAccessEnabled", "false"));

        var created = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: false));
        var world = await created.Content.ReadFromJsonAsync<WorldResponse>();

        var update = await client.PutAsJsonAsync($"/api/worlds/{world!.Id}", new UpdateWorldRequest(
            Name: world.Name, Description: null, GameSystem: null,
            PublicSlug: "demo-reach", PublicAccessEnabled: true));

        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task KillSwitch_StopsServingExistingPublicDemoWorlds()
    {
        // Publish while the switch is on…
        var client = CreateClientWithTemplate();
        var created = await client.PostAsJsonAsync("/api/worlds/demo", new CreateDemoWorldRequest(Tutorial: false));
        var world = await created.Content.ReadFromJsonAsync<WorldResponse>();

        var update = await client.PutAsJsonAsync($"/api/worlds/{world!.Id}", new UpdateWorldRequest(
            Name: world.Name, Description: null, GameSystem: null,
            PublicSlug: "demo-reach", PublicAccessEnabled: true));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var publicOn = await client.GetAsync("/api/public/worlds/demo-reach");
        Assert.That(publicOn.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // …then flip it off: the same slug goes dark without touching the world row.
        var offClient = CreateClientWithTemplate(("DemoWorlds:PublicAccessEnabled", "false"));
        var publicOff = await offClient.GetAsync("/api/public/worlds/demo-reach");
        Assert.That(publicOff.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    // ------------------------------------------------------------------ fixture zip --

    private static readonly Guid CampaignId = Guid.NewGuid();
    private static readonly Guid Source1Id = Guid.NewGuid();
    private static readonly Guid Source2Id = Guid.NewGuid();
    private static readonly Guid AttachmentId = Guid.NewGuid();
    private static readonly Guid Artifact1Id = Guid.NewGuid();
    private static readonly Guid Artifact2Id = Guid.NewGuid();
    private static readonly Guid FactId = Guid.NewGuid();
    private static readonly Guid RelationshipId = Guid.NewGuid();

    /// <summary>A minimal but complete template package in the world-export zip format.</summary>
    private static void WriteTemplateZip(string path, bool danglingPin = false)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);

        WriteEntry(zip, "world.json", $$"""
            {"id":"{{Guid.NewGuid()}}","name":"The Vespergale Reach",
             "description":"A ready-made demo campaign.","gameSystem":"D&D 5e"}
            """);

        WriteEntry(zip, "campaigns.json", $$"""
            {"campaigns":[{"id":"{{CampaignId}}","name":"The Vesper Bell","description":null,
              "status":"Active","startedAt":"2026-01-02T00:00:00Z","endedAt":null}],
             "campaignCharacters":[],"storylineCampaigns":[]}
            """);

        WriteEntry(zip, "sources.json", $$"""
            {"sources":[
              {"id":"{{Source1Id}}","campaignId":"{{CampaignId}}","type":"SessionNote",
               "title":"Session 1 - The dark lantern","body":"Third wreck in a month.",
               "uri":null,"occurredAt":"2026-01-02T00:00:00Z","visibility":"PartyVisible",
               "processingStatus":"Processed","extractionEnabled":true,"derivedText":null},
              {"id":"{{Source2Id}}","campaignId":null,"type":"GMNote",
               "title":"GM prep - The Castellan's design","body":"Voss is the last Bellwarden.",
               "uri":null,"occurredAt":null,"visibility":"GMOnly",
               "processingStatus":"Processed","extractionEnabled":false,"derivedText":null}],
             "extractions":[{"id":"{{Guid.NewGuid()}}","sourceId":"{{Source1Id}}",
               "extractionType":"Manual","text":"The party investigates wrecks.","confidence":0.9}],
             "references":[{"id":"{{Guid.NewGuid()}}","sourceId":"{{Source1Id}}",
               "targetType":"Artifact","targetId":"{{Artifact1Id}}","quote":null,"notes":null}]}
            """);

        WriteEntry(zip, "attachments.json", $$"""
            [{"id":"{{AttachmentId}}","sourceId":"{{Source1Id}}","kind":"MapImage",
              "fileName":"map.png","contentType":"image/png","sizeBytes":4,"ord":0,
              "status":"Stored","createdAt":"2026-01-01T00:00:00Z",
              "updatedAt":"2026-01-01T00:00:00Z",
              "file":"attachments/{{Source1Id}}/{{AttachmentId}}/map.png"}]
            """);

        var blobEntry = zip.CreateEntry($"attachments/{Source1Id}/{AttachmentId}/map.png");
        using (var blobStream = blobEntry.Open())
        {
            blobStream.Write("PNG!"u8);
        }

        WriteEntry(zip, "codex.json", $$"""
            {"artifacts":[
              {"id":"{{Artifact1Id}}","type":"Location","name":"Harrowport",
               "summary":"Port city.","visibility":"PartyVisible","confidence":0.95,"status":"Active"},
              {"id":"{{Artifact2Id}}","type":"Character","name":"Castellan Maren Voss",
               "summary":"Protector of the Reach.","visibility":"PartyVisible","confidence":0.9,"status":"Active"}],
             "facts":[{"id":"{{FactId}}","artifactId":"{{Artifact2Id}}","predicate":"is",
               "value":"the last Bellwarden","confidence":0.9,"truthState":"Confirmed","visibility":"GMOnly"}],
             "relationships":[{"id":"{{RelationshipId}}","artifactAId":"{{Artifact1Id}}",
               "artifactBId":"{{Artifact2Id}}","type":"ProtectedBy","description":null,
               "confidence":0.8,"truthState":"Confirmed","visibility":"PartyVisible"}]}
            """);

        var pinArtifact = danglingPin ? Guid.NewGuid() : Artifact1Id;
        WriteEntry(zip, "map-pins.json", $$"""
            [{"id":"{{Guid.NewGuid()}}","sourceAttachmentId":"{{AttachmentId}}",
              "artifactId":"{{pinArtifact}}","x":0.295,"y":0.615,"label":"HARROWPORT",
              "confidence":0.9}]
            """);
    }

    private static void WriteEntry(ZipArchive zip, string name, string json)
    {
        var entry = zip.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(json);
    }
}
