using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Library;

/// <summary>
/// Filing Library passages as an excerpt source (feature 26). GM-only at the API, and the
/// source that comes back reads like any other source — with the document and pages on it.
/// </summary>
[TestFixture]
public class LibraryExcerptEndpointTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private async Task<(LibraryDocument Document, List<LibraryChunk> Chunks)> SeedIndexedDocumentAsync(
        Guid worldId, Guid uploaderId, VisibilityScope visibility = VisibilityScope.PartyVisible)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();

        var now = DateTimeOffset.UtcNow;
        var document = new LibraryDocument
        {
            Id = Guid.NewGuid(),
            WorldId = worldId,
            Title = "Player's Guide",
            FileName = "guide.pdf",
            ContentType = "application/pdf",
            BlobPath = $"worlds/{worldId}/library/guide.pdf",
            Kind = LibraryDocumentKind.Sourcebook,
            Visibility = visibility,
            Status = LibraryDocumentStatus.Indexed,
            PageCount = 60,
            ChunkCount = 3,
            UploadedByUserId = uploaderId,
            CreatedAt = now,
            UpdatedAt = now
        };
        var chunks = new List<LibraryChunk>
        {
            new() { Id = Guid.NewGuid(), DocumentId = document.Id, WorldId = worldId, Ord = 0, Page = 42, Text = "Thistlehold sits on the river.", CreatedAt = now },
            new() { Id = Guid.NewGuid(), DocumentId = document.Id, WorldId = worldId, Ord = 1, Page = 43, Text = "The Thistle Council governs it.", CreatedAt = now },
            new() { Id = Guid.NewGuid(), DocumentId = document.Id, WorldId = worldId, Ord = 2, Page = 44, Text = "Its guilds trade downriver.", CreatedAt = now },
        };

        db.LibraryDocuments.Add(document);
        db.LibraryChunks.AddRange(chunks);
        await db.SaveChangesAsync();
        return (document, chunks);
    }

    [Test]
    [Category("Authorization")]
    public async Task Search_AsPlayer_Returns403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.PlayerClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/excerpts/search",
            new SearchLibraryExcerptsRequest(null, "Thistlehold"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Search_AsGm_ReturnsAList()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/excerpts/search",
            new SearchLibraryExcerptsRequest(null, "Thistlehold"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var candidates = await response.Content.ReadFromJsonAsync<List<LibraryExcerptCandidateResponse>>();
        Assert.That(candidates, Is.Not.Null);
    }

    [Test]
    [Category("Authorization")]
    public async Task File_AsPlayer_Returns403_EvenOnThePartyShelf()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var (document, chunks) = await SeedIndexedDocumentAsync(scenario.World.Id, scenario.GmUserId);

        var response = await scenario.PlayerClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/{document.Id}/excerpts",
            new FileLibraryExcerptRequest([chunks[0].Id], null, null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task File_AsGm_CreatesAQueuedExcerpt_ThatReadsBackWithItsDocumentAndPages()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var (document, chunks) = await SeedIndexedDocumentAsync(scenario.World.Id, scenario.GmUserId, VisibilityScope.GMOnly);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/{document.Id}/excerpts",
            new FileLibraryExcerptRequest([chunks[2].Id, chunks[0].Id], null, null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var filed = (await response.Content.ReadFromJsonAsync<LibraryExcerptFiledResponse>())!;
        Assert.That(filed.Title, Is.EqualTo("Player's Guide, pp. 42–44"));
        Assert.That(filed.ProcessingStatus, Is.EqualTo("Queued"));
        Assert.That(_factory.ExtractionQueueClient.SentMessages.Select(m => m.SourceId), Does.Contain(filed.SourceId));

        var read = await scenario.GmClient.GetFromJsonAsync<SourceResponse>(
            $"/api/worlds/{scenario.World.Id}/sources/{filed.SourceId}");
        Assert.That(read!.Type, Is.EqualTo("LibraryExcerpt"));
        Assert.That(read.Visibility, Is.EqualTo("GMOnly"), "the document's shelf is the excerpt's audience");
        Assert.That(read.LibraryDocumentId, Is.EqualTo(document.Id));
        Assert.That(read.LibraryDocumentTitle, Is.EqualTo("Player's Guide"));
        Assert.That((read.LibraryPageFrom, read.LibraryPageTo), Is.EqualTo((42, 44)));
        Assert.That(read.Body, Does.Contain("Thistlehold sits on the river.\n\nIts guilds trade downriver."));
    }

    [Test]
    public async Task File_ByPageRange_ForACodexEntry_TitlesTheExcerptForIt()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var (document, _) = await SeedIndexedDocumentAsync(scenario.World.Id, scenario.GmUserId);
        Guid artifactId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
            var artifact = new Artifact
            {
                Id = Guid.NewGuid(),
                WorldId = scenario.World.Id,
                Type = ArtifactType.Location,
                Name = "Thistlehold",
                Visibility = VisibilityScope.PartyVisible,
                Status = ArtifactStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.Artifacts.Add(artifact);
            await db.SaveChangesAsync();
            artifactId = artifact.Id;
        }

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/{document.Id}/excerpts",
            new FileLibraryExcerptRequest(null, 43, 44, artifactId));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var filed = (await response.Content.ReadFromJsonAsync<LibraryExcerptFiledResponse>())!;
        Assert.That(filed.Title, Is.EqualTo("Thistlehold — Player's Guide, pp. 43–44"));
    }

    [Test]
    public async Task File_DocumentOfAnotherWorld_Returns404()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var otherWorld = await SourceTestHelpers.CreateTestWorldAsync(_factory, scenario.GmUserId);
        var (document, chunks) = await SeedIndexedDocumentAsync(otherWorld.Id, scenario.GmUserId);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/{document.Id}/excerpts",
            new FileLibraryExcerptRequest([chunks[0].Id], null, null, null));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }
}
