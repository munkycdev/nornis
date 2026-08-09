using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Persistence;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

/// <summary>
/// POST /sources/{id}/transcribe — the read the capture page performs so a GM can correct
/// the handwriting before extraction runs on it. Covers who may ask and what a caller
/// waiting on the answer gets back.
/// </summary>
[TestFixture]
public class SourceTranscribeEndpointTests
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

    /// <summary>Stores a page image the way a confirmed upload leaves it: a Stored row with
    /// bytes at its blob path.</summary>
    private async Task SeedStoredPageAsync(Guid worldId, Guid sourceId)
    {
        var blobPath = $"worlds/{worldId}/sources/{sourceId}/000-page-1.jpg";

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        db.SourceAttachments.Add(new SourceAttachment
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            WorldId = worldId,
            Kind = SourceAttachmentKind.PageImage,
            FileName = "page-1.jpg",
            ContentType = "image/jpeg",
            SizeBytes = 42,
            BlobPath = blobPath,
            Ord = 0,
            Status = SourceAttachmentStatus.Stored,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        _factory.BlobStorage.Blobs[blobPath] = (new byte[42], "image/jpeg");
    }

    private async Task<string?> ReadStoredBodyAsync(Guid sourceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NornisDbContext>();
        return (await db.Sources.FindAsync(sourceId))!.Body;
    }

    [Test]
    public async Task Transcribe_ReturnsTheReading_AndStoresItWithoutSendingForExtraction()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);
        _factory.Transcription.MarkdownToReturn = "# Session 4\n\nCaptain Voss lied about the Silver Key.";

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<TranscribeSourceResponse>();
        Assert.That(body!.Markdown, Is.EqualTo("# Session 4\n\nCaptain Voss lied about the Silver Key."));
        Assert.That(await ReadStoredBodyAsync(source.Id),
            Is.EqualTo("# Session 4\n\nCaptain Voss lied about the Silver Key."));
        Assert.That(_factory.ExtractionQueueClient.SentMessages, Is.Empty,
            "reading is not sending — the GM still has to send it");
    }

    [Test]
    public async Task Transcribe_Twice_DoesNotBuyASecondReading()
    {
        // The second press is what would overwrite a correction, and it is also what a
        // double-tap on a phone produces.
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);
        _factory.Transcription.MarkdownToReturn = "Captain Vass at the docks.";

        await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        // The GM fixes the name, as the feature intends.
        await scenario.GmClient.PutAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}",
            new { Body = "Captain Voss at the docks." });

        var second = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await second.Content.ReadFromJsonAsync<TranscribeSourceResponse>();
        Assert.That(body!.Markdown, Is.EqualTo("Captain Voss at the docks."), "the correction survived");
        Assert.That(_factory.Transcription.CallCount, Is.EqualTo(1), "one paid reading, not two");
    }

    [Test]
    public async Task Transcribe_WithNoPageImages_400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("no_page_images"));
        Assert.That(_factory.Transcription.CallCount, Is.Zero);
    }

    [Test]
    public async Task Transcribe_ASourceThatIsNotHandwriting_400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.Upload, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("invalid_source_type"));
    }

    [Test]
    public async Task Transcribe_ASourceAlreadyQueued_409()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Queued);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(_factory.Transcription.CallCount, Is.Zero);
    }

    [Test]
    public async Task Transcribe_AsAnObserver_403_AndBuysNothing()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);

        var response = await scenario.ObserverClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.Transcription.CallCount, Is.Zero,
            "an unauthorized request must not reach a paid call");
    }

    [Test]
    public async Task Transcribe_AsAPlayerWhoDoesNotOwnTheSource_403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);

        var response = await scenario.PlayerClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That(_factory.Transcription.CallCount, Is.Zero);
    }

    [Test]
    public async Task Transcribe_Anonymously_401()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);

        var anonymous = _factory.CreateClient();
        var response = await anonymous.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Transcribe_PagesThatReadAsBlank_200WithEmptyText()
    {
        // Retrying cannot help, so this is not a failure — the page says so and offers a
        // retake instead.
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);
        _factory.Transcription.MarkdownToReturn = "   ";

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<TranscribeSourceResponse>();
        Assert.That(body!.Markdown, Is.Empty);
        Assert.That(await ReadStoredBodyAsync(source.Id), Is.Null);
    }

    [Test]
    public async Task Transcribe_WhenTheReadingServiceIsDown_503()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);
        await SeedStoredPageAsync(scenario.World.Id, source.Id);
        _factory.Transcription.ExceptionToThrow = new TimeoutException("vision call timed out");

        var response = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/transcribe", null);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.That(error!.Code, Is.EqualTo("ai_unavailable"));
    }
}
