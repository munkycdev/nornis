using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Api.Tests.Sources;

[TestFixture]
public class SourceAttachmentEndpointTests
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

    private static RequestSourceAttachmentUploadRequest PageRequest(string fileName = "page-1.jpg", int ord = 0) =>
        new(fileName, "image/jpeg", 5000, "PageImage", ord);

    [Test]
    public async Task FullHandshake_RequestPutConfirmList_Works()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);

        var ticketResponse = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/request-upload", PageRequest());
        Assert.That(ticketResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ticketResponse.Content.ReadAsStringAsync());
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<SourceAttachmentUploadResponse>();

        // Simulate the browser PUT.
        _factory.BlobStorage.Blobs[$"worlds/{scenario.World.Id}/sources/{source.Id}/000-page-1.jpg"] =
            (new byte[42], "image/jpeg");

        var confirmResponse = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/{ticket!.Attachment.Id}/confirm", null);
        Assert.That(confirmResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var confirmed = await confirmResponse.Content.ReadFromJsonAsync<SourceAttachmentResponse>();
        Assert.That(confirmed!.Status, Is.EqualTo("Stored"));
        Assert.That(confirmed.SizeBytes, Is.EqualTo(42));

        var list = await scenario.GmClient.GetFromJsonAsync<List<SourceAttachmentResponse>>(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments");
        Assert.That(list!, Has.Count.EqualTo(1));
        Assert.That(list![0].Url, Does.Contain("sas=download"));
    }

    [Test]
    public async Task Confirm_WithoutBlob_400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);

        var ticketResponse = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/request-upload", PageRequest());
        var ticket = await ticketResponse.Content.ReadFromJsonAsync<SourceAttachmentUploadResponse>();

        var confirmResponse = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/{ticket!.Attachment.Id}/confirm", null);

        Assert.That(confirmResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RequestUpload_UnsupportedType_400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/request-upload",
            PageRequest(fileName: "notes.pdf"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RequestUpload_QueuedSource_409()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Queued);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/request-upload", PageRequest());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
    }

    [Test]
    public async Task RequestUpload_NonOwnerPlayer_403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var source = await SourceTestHelpers.CreateTestSourceAsync(
            _factory, scenario.World.Id, scenario.GmUserId,
            type: SourceType.HandwrittenNotes, processingStatus: SourceProcessingStatus.Draft);

        var response = await scenario.PlayerClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/sources/{source.Id}/attachments/request-upload", PageRequest());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
