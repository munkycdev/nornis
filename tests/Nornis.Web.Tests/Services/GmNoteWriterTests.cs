using System.Net;
using System.Text;
using System.Text.Json;
using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// A GM note only changes canon if it reaches extraction, which means both halves of the
/// sequence — create the source, then mark it ready — have to happen. A note that is created
/// and never queued looks identical to the GM and does nothing.
/// </summary>
[TestFixture]
public class GmNoteWriterTests
{
    private static readonly Guid WorldId = Guid.NewGuid();

    private static (GmNoteWriter Writer, RecordingHandler Handler) Build(
        HttpStatusCode createStatus = HttpStatusCode.Created,
        HttpStatusCode readyStatus = HttpStatusCode.OK)
    {
        var handler = new RecordingHandler(createStatus, readyStatus);
        var api = new NornisApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            new ViewAsState(),
            new ActivitySignal());
        return (new GmNoteWriter(api), handler);
    }

    [Test]
    public async Task Saving_CreatesTheNoteAndQueuesIt()
    {
        var (writer, handler) = Build();

        var result = await writer.SaveAsync(WorldId, subject: null, "Voss was lying.", "PartyVisible");

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(handler.CreatedBody, Is.Not.Null);
        Assert.That(handler.MarkedReadySourceId, Is.EqualTo(handler.NewSourceId),
            "a note that is never marked ready is never read, and proposes nothing");
    }

    [Test]
    public async Task Saving_FilesTheNoteAsAGmNote()
    {
        // The type is what routes it in the sources ledger and marks it as the GM's own word
        // rather than something captured at the table.
        var (writer, handler) = Build();

        await writer.SaveAsync(WorldId, subject: null, "Voss was lying.", "PartyVisible");

        Assert.That(handler.CreatedBody!.RootElement.GetProperty("type").GetString(), Is.EqualTo("GMNote"));
    }

    [Test]
    public async Task Saving_SendsTheChosenVisibility()
    {
        // GMOnly notes must not silently become party-visible: the whole point of the choice is
        // that some corrections are not for the players.
        var (writer, handler) = Build();

        await writer.SaveAsync(WorldId, subject: null, "He is the traitor.", "GMOnly");

        Assert.That(handler.CreatedBody!.RootElement.GetProperty("visibility").GetString(), Is.EqualTo("GMOnly"));
    }

    [Test]
    public async Task ASubject_IsNamedInTheBody()
    {
        // Extraction matches a note to an artifact by the names in its text. Without the prefix
        // a note written on Voss's page proposes changes against nothing.
        var (writer, handler) = Build();

        await writer.SaveAsync(WorldId, "Captain Voss", "was never in Black Harbor.", "PartyVisible");

        Assert.That(handler.CreatedBody!.RootElement.GetProperty("body").GetString(),
            Is.EqualTo("Regarding Captain Voss: was never in Black Harbor."));
    }

    [Test]
    public void NoSubject_LeavesTheBodyAsWritten()
    {
        var request = GmNoteWriter.BuildRequest(subject: null, "  The caravan reached port.  ", "PartyVisible");

        Assert.That(request.Body, Is.EqualTo("The caravan reached port."));
        Assert.That(request.Title, Does.StartWith("GM note — "));
    }

    [Test]
    public async Task AFailedCreate_DoesNotQueueAnything()
    {
        var (writer, handler) = Build(createStatus: HttpStatusCode.BadRequest);

        var result = await writer.SaveAsync(WorldId, subject: null, "…", "PartyVisible");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(handler.MarkedReadySourceId, Is.Null);
    }

    [Test]
    public async Task ANoteThatSavedButCouldNotQueue_SaysSo()
    {
        // The GM's text is safe as a draft. Reporting a bare failure would invite them to
        // rewrite a note that already exists.
        var (writer, _) = Build(readyStatus: HttpStatusCode.BadGateway);

        var result = await writer.SaveAsync(WorldId, subject: null, "Voss was lying.", "PartyVisible");

        Assert.That(result.IsSuccess, Is.False);
        Assert.That(result.Error!.Message, Does.Contain("draft"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _createStatus;
        private readonly HttpStatusCode _readyStatus;

        public RecordingHandler(HttpStatusCode createStatus, HttpStatusCode readyStatus)
        {
            _createStatus = createStatus;
            _readyStatus = readyStatus;
        }

        public Guid NewSourceId { get; } = Guid.NewGuid();
        public JsonDocument? CreatedBody { get; private set; }
        public Guid? MarkedReadySourceId { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;

            if (path == $"/api/worlds/{WorldId}/sources")
            {
                CreatedBody = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                return _createStatus == HttpStatusCode.Created
                    ? Json(_createStatus, SourceJson(NewSourceId))
                    : Problem(_createStatus);
            }

            if (path == $"/api/worlds/{WorldId}/sources/{NewSourceId}/ready")
            {
                MarkedReadySourceId = NewSourceId;
                return _readyStatus == HttpStatusCode.OK
                    ? Json(_readyStatus, SourceJson(NewSourceId))
                    : Problem(_readyStatus);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string SourceJson(Guid id) =>
            $$"""
            {"id":"{{id}}","worldId":"{{WorldId}}","type":"GMNote","title":"GM note",
             "body":null,"uri":null,"occurredAt":null,"createdAt":"2026-07-29T00:00:00+00:00",
             "visibility":"PartyVisible","processingStatus":"Draft"}
            """;

        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

        private static HttpResponseMessage Problem(HttpStatusCode status) => new(status)
        {
            Content = new StringContent(
                """{"title":"nope","detail":"the API said no"}""", Encoding.UTF8, "application/problem+json"),
        };
    }
}
