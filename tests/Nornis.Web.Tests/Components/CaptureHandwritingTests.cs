using System.Net;
using System.Text;
using System.Text.Json;
using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using MudBlazor.Services;
using Nornis.Web.ApiClient;
using Nornis.Web.Components.Pages;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Components;

/// <summary>
/// The read-before-extract flow on the capture page. What is worth pinning here is the order
/// of the steps rather than the styling: the editor must not appear before there is a reading
/// to correct (typing into it early means typing into something the transcription overwrites),
/// the reading has to actually land on screen, and nothing may be sent for extraction until
/// the GM presses the send button.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public class CaptureHandwritingTests : BunitContext
{
    private StubHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHandler();
        var viewAs = new ViewAsState();
        var signal = new ActivitySignal();
        var api = new NornisApiClient(
            new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") }, viewAs, signal,
            new AuthSessionState());

        Services.AddMudServices();
        Services.AddSingleton(viewAs);
        Services.AddSingleton(signal);
        Services.AddSingleton(api);
        Services.AddSingleton(sp => new WorldState(api, viewAs, sp.GetRequiredService<IJSRuntime>()));
        // The capture page's rail hosts the Loremaster panel, which brings its own state.
        Services.AddScoped<AskState>();
        Services.AddScoped(sp => new AskHistoryStore(
            sp.GetRequiredService<IJSRuntime>(), sp.GetRequiredService<MudBlazor.ISnackbar>()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        // The camera hand-off: the page asks the browser to re-encode what was picked and
        // describe the bytes it will actually upload.
        JSInterop.Setup<PickedFile[]>("nornisUpload.prepareImages", _ => true)
            .SetResult([new PickedFile("photo.jpg", 512_000, "image/jpeg")]);
        JSInterop.Setup<bool>("nornisUpload.sendAt", _ => true).SetResult(true);
        // The editor's content when the page reads it back at save time. Standing in for a GM
        // who fixed the name the model misread: the stub returns "Captain Vass", this is what
        // is on screen when they press save.
        JSInterop.Setup<string>("nornisEditor.getHtml", _ => true)
            .SetResult("<p>Captain Voss at Black Harbor.</p>");

        Renderer.SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    [TearDown]
    public async Task TearDown() => await DisposeAsync();

    private async Task<IRenderedComponent<Capture>> RenderCaptureAsync()
    {
        var worlds = Services.GetRequiredService<WorldState>();
        await worlds.EnsureSelectionRestoredAsync();

        // Type arrives from the query string, the way the dashboard's capture tiles link in.
        Services.GetRequiredService<NavigationManager>().NavigateTo("/capture?type=HandwrittenNotes");
        return Render<Capture>();
    }

    private static bool HasEditor(IRenderedComponent<Capture> cut) =>
        cut.FindAll(".nornis-editor").Count > 0;

    private static IElement? FindButton(IRenderedComponent<Capture> cut, string text) =>
        cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains(text));

    /// <summary>
    /// Selects a photo. The page does not read the bytes Blazor hands it for handwriting — it
    /// asks the browser to re-encode the camera's output first — so the content here only has
    /// to exist; the mocked prepareImages is what describes the upload.
    /// </summary>
    private static void PickPhoto(IRenderedComponent<Capture> cut) =>
        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("pretend HEIC", "IMG_0042.HEIC", contentType: "image/heic"));

    private static async Task ReadAsync(IRenderedComponent<Capture> cut) =>
        await cut.InvokeAsync(() => FindButton(cut, "Read the handwriting")!.ClickAsync(new()));

    private static async Task PickAndReadAsync(IRenderedComponent<Capture> cut)
    {
        PickPhoto(cut);
        await ReadAsync(cut);
    }

    private static void EnterTitle(IRenderedComponent<Capture> cut, string title) =>
        cut.Find("input[type=text]").Input(title);

    [Test]
    public async Task BeforeAnyPhoto_ThereIsNoEditorAndNothingToRead()
    {
        var cut = await RenderCaptureAsync();

        Assert.That(HasEditor(cut), Is.False,
            "an editor here would collect corrections to a reading that does not exist yet");
        Assert.That(FindButton(cut, "Read the handwriting"), Is.Null);
        Assert.That(FindButton(cut, "Take a photo"), Is.Not.Null);
    }

    [Test]
    public async Task WithAPhotoPicked_TheReadButtonAppears_ButStillNoEditor()
    {
        var cut = await RenderCaptureAsync();
        PickPhoto(cut);

        Assert.That(FindButton(cut, "Read the handwriting"), Is.Not.Null);
        Assert.That(HasEditor(cut), Is.False);
        Assert.That(cut.Markup, Does.Contain("photo.jpg"), "the page names what it is about to read");
    }

    [Test]
    public async Task Reading_ShowsTheTranscriptionInAnEditorAndSendsNothingForExtraction()
    {
        var cut = await RenderCaptureAsync();
        PickPhoto(cut);
        EnterTitle(cut, "Session 4 notes");

        await ReadAsync(cut);

        Assert.That(_handler.TranscribedSourceId, Is.EqualTo(_handler.NewSourceId),
            "the draft has to exist before there is anywhere to hang the photos");
        Assert.That(HasEditor(cut), Is.True, "the reading has to be correctable, so it has to be on screen");
        Assert.That(cut.Markup, Does.Contain("Check the text below before saving."));
        Assert.That(_handler.MarkedReadySourceId, Is.Null,
            "reading is not sending — extraction waits for the GM");
    }

    [Test]
    public async Task ReadingWithoutATitle_AsksForOneInsteadOfBuyingTheRead()
    {
        // The draft is created by this step and a source needs a title, so the failure would
        // otherwise land after the vision call had been paid for.
        var cut = await RenderCaptureAsync();
        PickPhoto(cut);

        await ReadAsync(cut);

        Assert.That(_handler.TranscribedSourceId, Is.Null);
        Assert.That(cut.Markup, Does.Contain("Title is required"));
        Assert.That(HasEditor(cut), Is.False);
    }

    [Test]
    public async Task ABlankReading_SaysSo_RatherThanLeavingAnEmptyBox()
    {
        var cut = await RenderCaptureAsync();
        _handler.TranscriptionMarkdown = string.Empty;
        EnterTitle(cut, "Session 4 notes");

        await PickAndReadAsync(cut);

        Assert.That(cut.Markup, Does.Contain("no readable handwriting"));
        Assert.That(HasEditor(cut), Is.True, "they can still type the notes themselves");
    }

    [Test]
    public async Task AFailedReading_SaysWhatHappened_AndStaysRetryable()
    {
        var cut = await RenderCaptureAsync();
        _handler.TranscribeStatus = HttpStatusCode.ServiceUnavailable;
        EnterTitle(cut, "Session 4 notes");

        await PickAndReadAsync(cut);

        Assert.That(cut.Markup, Does.Contain("the API said no"),
            "silence after a click is indistinguishable from a dead button");
        Assert.That(FindButton(cut, "Read the handwriting"), Is.Not.Null, "still retryable");
        Assert.That(HasEditor(cut), Is.False);
    }

    [Test]
    public async Task SavingAfterAReading_SendsTheCorrectedTextAndQueuesItOnce()
    {
        var cut = await RenderCaptureAsync();
        EnterTitle(cut, "Session 4 notes");
        await PickAndReadAsync(cut);

        await cut.InvokeAsync(() => FindButton(cut, "Save & process")!.ClickAsync(new()));

        Assert.That(_handler.CreateCount, Is.EqualTo(1),
            "the reading and the save share one draft — two sources would split the photos "
            + "from the words that came out of them");
        Assert.That(_handler.MarkedReadySourceId, Is.EqualTo(_handler.NewSourceId));

        // The whole point: what goes for extraction is what the GM approved, not what the
        // model guessed. The reading said "Vass".
        Assert.That(_handler.UpdatedBodies, Has.Count.EqualTo(1), "saving updates the draft it made");
        Assert.That(_handler.UpdatedBodies[0], Does.Contain("Captain Voss"));
        Assert.That(_handler.UpdatedBodies[0], Does.Not.Contain("Vass"));
    }

    [Test]
    public async Task Retaking_ClearsTheStoredReadingAlongWithThePhotos()
    {
        // The stored body is the guard that stops the pipeline reading again; leaving it would
        // extract text attributed to pages that are gone.
        var cut = await RenderCaptureAsync();
        EnterTitle(cut, "Session 4 notes");
        await PickAndReadAsync(cut);

        await cut.InvokeAsync(() => FindButton(cut, "Retake")!.ClickAsync(new()));

        Assert.That(_handler.DeletedAttachmentIds, Has.Count.EqualTo(1));
        Assert.That(_handler.ClearedBody, Is.True);
        Assert.That(HasEditor(cut), Is.False, "back to the start");
        Assert.That(FindButton(cut, "Take a photo"), Is.Not.Null);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Guid WorldId { get; } = Guid.NewGuid();
        public Guid NewSourceId { get; } = Guid.NewGuid();
        public Guid AttachmentId { get; } = Guid.NewGuid();

        public string TranscriptionMarkdown { get; set; } = "# Session 4\n\nCaptain Vass at Black Harbor.";
        public HttpStatusCode TranscribeStatus { get; set; } = HttpStatusCode.OK;

        public int CreateCount { get; private set; }
        public Guid? TranscribedSourceId { get; private set; }
        public Guid? MarkedReadySourceId { get; private set; }
        public List<string?> UpdatedBodies { get; } = [];
        public List<Guid> DeletedAttachmentIds { get; } = [];
        public bool ClearedBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var root = $"/api/worlds/{WorldId}/sources";

            if (path == "/api/worlds")
            {
                return Json(HttpStatusCode.OK,
                    $$"""
                    [{"id":"{{WorldId}}","name":"Vespergale Reach","description":null,
                      "gameSystem":null,"myRole":"GM"}]
                    """);
            }

            if (path == $"/api/worlds/{WorldId}/campaigns")
            {
                return Json(HttpStatusCode.OK, "[]");
            }

            if (path == root && request.Method == HttpMethod.Post)
            {
                CreateCount++;
                return Json(HttpStatusCode.Created, SourceJson());
            }

            if (path == $"{root}/{NewSourceId}" && request.Method == HttpMethod.Put)
            {
                var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
                if (body.TryGetProperty("clearBody", out var clear) && clear.GetBoolean())
                {
                    ClearedBody = true;
                }
                else
                {
                    UpdatedBodies.Add(body.TryGetProperty("body", out var b) ? b.GetString() : null);
                }

                return Json(HttpStatusCode.OK, SourceJson());
            }

            if (path == $"{root}/{NewSourceId}/attachments/request-upload")
            {
                return Json(HttpStatusCode.OK,
                    $$"""
                    {"attachment":{"id":"{{AttachmentId}}","sourceId":"{{NewSourceId}}",
                      "kind":"PageImage","fileName":"photo.jpg","contentType":"image/jpeg",
                      "sizeBytes":512000,"ord":0,"status":"Pending",
                      "createdAt":"2026-08-09T00:00:00+00:00"},
                     "uploadUrl":"http://blob.test/put?sas=upload"}
                    """);
            }

            if (path == $"{root}/{NewSourceId}/attachments/{AttachmentId}/confirm")
            {
                return Json(HttpStatusCode.OK, AttachmentJson());
            }

            if (path == $"{root}/{NewSourceId}/attachments" && request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, $"[{AttachmentJson()}]");
            }

            if (path == $"{root}/{NewSourceId}/attachments/{AttachmentId}"
                && request.Method == HttpMethod.Delete)
            {
                DeletedAttachmentIds.Add(AttachmentId);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == $"{root}/{NewSourceId}/transcribe")
            {
                if (TranscribeStatus != HttpStatusCode.OK)
                {
                    return Problem(TranscribeStatus);
                }

                TranscribedSourceId = NewSourceId;
                return Json(HttpStatusCode.OK,
                    $$"""{"markdown":{{JsonSerializer.Serialize(TranscriptionMarkdown)}}}""");
            }

            if (path == $"{root}/{NewSourceId}/ready")
            {
                MarkedReadySourceId = NewSourceId;
                return Json(HttpStatusCode.OK, SourceJson());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private string SourceJson() =>
            $$"""
            {"id":"{{NewSourceId}}","worldId":"{{WorldId}}","type":"HandwrittenNotes",
             "title":"Session 4 notes","body":null,"uri":null,"occurredAt":null,
             "createdAt":"2026-08-09T00:00:00+00:00","visibility":"PartyVisible",
             "processingStatus":"Draft"}
            """;

        private string AttachmentJson() =>
            $$"""
            {"id":"{{AttachmentId}}","sourceId":"{{NewSourceId}}","kind":"PageImage",
             "fileName":"photo.jpg","contentType":"image/jpeg","sizeBytes":512000,"ord":0,
             "status":"Stored","createdAt":"2026-08-09T00:00:00+00:00"}
            """;

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        // The API's real error dialect ({code, message}) — a shape mismatch would yield null
        // members and a silently empty message, which is what the failure test guards against.
        private static HttpResponseMessage Problem(HttpStatusCode status) => new(status)
        {
            Content = new StringContent(
                """{"code":"ai_unavailable","message":"the API said no"}""",
                Encoding.UTF8, "application/json"),
        };
    }
}
