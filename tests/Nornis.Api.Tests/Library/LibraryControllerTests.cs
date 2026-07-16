using System.Net;
using System.Net.Http.Json;
using Nornis.Api.Contracts.Requests;
using Nornis.Api.Contracts.Responses;
using Nornis.Api.Tests.Infrastructure;
using NUnit.Framework;

namespace Nornis.Api.Tests.Library;

[TestFixture]
public class LibraryControllerTests
{
    private NornisWebApplicationFactory _factory = null!;

    [SetUp]
    public void SetUp() => _factory = new NornisWebApplicationFactory();

    [TearDown]
    public void TearDown() => _factory.Dispose();

    private static RequestLibraryUploadRequest UploadRequest(
        string title = "Forbidden Depths",
        string fileName = "depths.pdf",
        string contentType = "application/pdf",
        long size = 2048,
        string visibility = "GMOnly") =>
        new(title, fileName, contentType, size, "Sourcebook", visibility);

    private async Task<LibraryUploadResponse> RequestUploadAsync(HttpClient client, Guid worldId, RequestLibraryUploadRequest request)
    {
        var response = await client.PostAsJsonAsync($"/api/worlds/{worldId}/library/request-upload", request);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return (await response.Content.ReadFromJsonAsync<LibraryUploadResponse>())!;
    }

    private void SimulateBrowserPut(LibraryUploadResponse ticket, int size = 2048, string contentType = "application/pdf")
    {
        // Extract the blob path from the fake SAS URL: https://blob.test/{path}?sas=upload
        var path = new Uri(ticket.UploadUrl).AbsolutePath.TrimStart('/');
        _factory.BlobStorage.Blobs[path] = (new byte[size], contentType);
    }

    [Test]
    public async Task FullHandshake_PdfUpload_EndsInIndexingWithRealSize()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var ticket = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest(size: 999));
        Assert.That(ticket.Document.Status, Is.EqualTo("PendingUpload"));
        SimulateBrowserPut(ticket, size: 4096);

        var confirm = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}/confirm", null);
        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var confirmed = await confirm.Content.ReadFromJsonAsync<LibraryDocumentResponse>();

        Assert.That(confirmed!.Status, Is.EqualTo("Indexing"));
        Assert.That(confirmed.SizeBytes, Is.EqualTo(4096), "size must come from storage, not the client");
    }

    [Test]
    public async Task RequestUpload_AsObserver_Returns403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.ObserverClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/request-upload", UploadRequest());

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RequestUpload_UnsupportedExtension_Returns400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var response = await scenario.GmClient.PostAsJsonAsync(
            $"/api/worlds/{scenario.World.Id}/library/request-upload",
            UploadRequest(fileName: "virus.exe", contentType: "application/octet-stream"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task RequestUpload_PlayerAskingGmOnly_ClampsToPartyVisible()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var ticket = await RequestUploadAsync(scenario.PlayerClient, scenario.World.Id, UploadRequest(visibility: "GMOnly"));

        Assert.That(ticket.Document.Visibility, Is.EqualTo("PartyVisible"));
    }

    [Test]
    public async Task Confirm_WithoutBlob_Returns400()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var ticket = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest());

        var confirm = await scenario.GmClient.PostAsync(
            $"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}/confirm", null);

        Assert.That(confirm.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task List_AsPlayer_HidesGmOnlyDocuments()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);

        var gmDoc = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest(title: "GM module", visibility: "GMOnly"));
        SimulateBrowserPut(gmDoc);
        await scenario.GmClient.PostAsync($"/api/worlds/{scenario.World.Id}/library/{gmDoc.Document.Id}/confirm", null);

        var partyDoc = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest(title: "Party handout", visibility: "PartyVisible"));
        SimulateBrowserPut(partyDoc);
        await scenario.GmClient.PostAsync($"/api/worlds/{scenario.World.Id}/library/{partyDoc.Document.Id}/confirm", null);

        var asPlayer = await scenario.PlayerClient.GetFromJsonAsync<List<LibraryDocumentResponse>>(
            $"/api/worlds/{scenario.World.Id}/library");
        var asGm = await scenario.GmClient.GetFromJsonAsync<List<LibraryDocumentResponse>>(
            $"/api/worlds/{scenario.World.Id}/library");

        Assert.That(asPlayer!.Select(d => d.Title), Is.EquivalentTo(new[] { "Party handout" }));
        Assert.That(asGm!.Select(d => d.Title), Is.EquivalentTo(new[] { "GM module", "Party handout" }));
    }

    [Test]
    public async Task Download_ReturnsSasUrl()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var ticket = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest());
        SimulateBrowserPut(ticket);
        await scenario.GmClient.PostAsync($"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}/confirm", null);

        var download = await scenario.GmClient.GetFromJsonAsync<LibraryDownloadResponse>(
            $"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}/download");

        Assert.That(download!.DownloadUrl, Does.Contain("sas=download"));
        Assert.That(download.FileName, Is.EqualTo("depths.pdf"));
    }

    [Test]
    public async Task Delete_PlayerDeletingGmUpload_Returns403()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var ticket = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest(visibility: "PartyVisible"));
        SimulateBrowserPut(ticket);
        await scenario.GmClient.PostAsync($"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}/confirm", null);

        var response = await scenario.PlayerClient.DeleteAsync(
            $"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task Delete_AsGm_RemovesDocumentAndBlob()
    {
        var scenario = await SourceTestHelpers.SetupFullScenarioAsync(_factory);
        var ticket = await RequestUploadAsync(scenario.GmClient, scenario.World.Id, UploadRequest());
        SimulateBrowserPut(ticket);
        await scenario.GmClient.PostAsync($"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}/confirm", null);

        var response = await scenario.GmClient.DeleteAsync(
            $"/api/worlds/{scenario.World.Id}/library/{ticket.Document.Id}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(_factory.BlobStorage.Blobs, Is.Empty);
        var list = await scenario.GmClient.GetFromJsonAsync<List<LibraryDocumentResponse>>(
            $"/api/worlds/{scenario.World.Id}/library");
        Assert.That(list, Is.Empty);
    }
}
