using System.IO.Pipelines;
using System.Net;
using System.Text;
using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using Nornis.Web.State;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// Reading a streamed Loremaster answer: <see cref="NornisApiClient.StreamAskAsync"/> turning the
/// API's server-sent events into <see cref="AskStreamEvent"/>s, and <see cref="StreamingAnswer"/>
/// deciding what the Ask surfaces render as they arrive.
/// </summary>
[TestFixture]
public class AskStreamTests
{
    private static readonly Guid WorldId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private const string DoneJson = """
        {"answer":"Captain Voss runs the harbor. [ref:art-1]","citations":[{"referenceId":"art-1","type":"Artifact","displayName":"Captain Voss","artifactId":"11111111-1111-1111-1111-111111111111","factId":null,"relationshipId":null,"sourceId":null,"documentId":null,"slug":"captain-voss"}],"confidence":"High","caveats":["Some information is marked as rumor"]}
        """;

    #region NornisApiClient.StreamAskAsync

    [Test]
    public async Task StreamAsk_ReadsStartDeltasAndDone_InOrder()
    {
        var api = Client(_ => Sse(
            Event("start", """{"confidence":"High","caveats":["Some information is marked as rumor"]}"""),
            Event("delta", """{"text":"Captain Voss "}"""),
            Event("delta", """{"text":"runs the harbor. [ref:art-1]"}"""),
            Event("done", DoneJson)));

        var events = await ToListAsync(api.StreamAskAsync(WorldId, "Who runs the harbor?"));

        Assert.That(events, Has.Count.EqualTo(4));
        Assert.Multiple(() =>
        {
            var started = (AskStreamEvent.Started)events[0];
            Assert.That(started.Confidence, Is.EqualTo("High"));
            Assert.That(started.Caveats, Is.EqualTo(["Some information is marked as rumor"]));
            Assert.That(events[1], Is.EqualTo(new AskStreamEvent.Delta("Captain Voss ")));
            Assert.That(events[2], Is.EqualTo(new AskStreamEvent.Delta("runs the harbor. [ref:art-1]")));
            var done = (AskStreamEvent.Done)events[3];
            Assert.That(done.Answer.Confidence, Is.EqualTo("High"));
            Assert.That(done.Answer.Citations.Single().Slug, Is.EqualTo("captain-voss"));
        });
    }

    [Test]
    public async Task StreamAsk_DeliversTheFirstWords_BeforeTheAnswerIsFinished()
    {
        // The whole point of streaming. HttpClient's default completion option buffers the entire
        // body before SendAsync returns, which would hold every word back until the last — this
        // stream is not finished until the test says so, so a buffering client never gets here.
        var pipe = new Pipe();
        var api = Client(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(pipe.Reader.AsStream()),
            };
            response.Content.Headers.ContentType = new("text/event-stream");
            return response;
        });

        await WriteAsync(pipe, Event("start", """{"confidence":"Low","caveats":[]}""") + Event("delta", """{"text":"The record "}"""));

        await using var events = api.StreamAskAsync(WorldId, "Q").GetAsyncEnumerator();
        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(patience.Token), Is.True);
        Assert.That(await events.MoveNextAsync().AsTask().WaitAsync(patience.Token), Is.True);
        Assert.That(events.Current, Is.EqualTo(new AskStreamEvent.Delta("The record ")),
            "the first delta arrived while the answer was still being written");

        await WriteAsync(pipe, Event("done", DoneJson));
        await pipe.Writer.CompleteAsync();
        Assert.That(await events.MoveNextAsync(), Is.True);
        Assert.That(events.Current, Is.TypeOf<AskStreamEvent.Done>());
    }

    [Test]
    public async Task StreamAsk_ARefusalBeforeTheStream_EndsWithTheApisOwnError()
    {
        var api = Client(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("""{"code":"invalid_question","message":"Question cannot be empty."}""", Encoding.UTF8, "application/json"),
        });

        var events = await ToListAsync(api.StreamAskAsync(WorldId, " "));

        Assert.That(events, Is.EqualTo(new AskStreamEvent[]
        {
            new AskStreamEvent.Failed(new ApiError("invalid_question", "Question cannot be empty.")),
        }));
    }

    [Test]
    public async Task StreamAsk_AnErrorEvent_EndsTheStreamWithThatError()
    {
        var api = Client(_ => Sse(
            Event("start", """{"confidence":"Medium","caveats":[]}"""),
            Event("delta", """{"text":"Accounts "}"""),
            Event("error", """{"code":"service_unavailable","message":"The Loremaster is temporarily unavailable. Please try again."}"""),
            Event("delta", """{"text":"never read"}""")));

        var events = await ToListAsync(api.StreamAskAsync(WorldId, "Q"));

        Assert.That(events[^1], Is.EqualTo(new AskStreamEvent.Failed(
            new ApiError("service_unavailable", "The Loremaster is temporarily unavailable. Please try again."))));
        Assert.That(events, Has.Count.EqualTo(3), "nothing is read past the end");
    }

    [Test]
    public async Task StreamAsk_AStreamThatStopsWithoutDone_EndsAsInterrupted()
    {
        var api = Client(_ => Sse(
            Event("start", """{"confidence":"Medium","caveats":[]}"""),
            Event("delta", """{"text":"Half an ans"}""")));

        var events = await ToListAsync(api.StreamAskAsync(WorldId, "Q"));

        Assert.That(events[^1], Is.TypeOf<AskStreamEvent.Failed>());
        Assert.That(((AskStreamEvent.Failed)events[^1]).Error.Code, Is.EqualTo("stream_interrupted"));
    }

    [Test]
    public async Task StreamAsk_AnEventItDoesNotKnow_IsSkipped()
    {
        var api = Client(_ => Sse(
            Event("start", """{"confidence":"Low","caveats":[]}"""),
            Event("thinking", """{"summary":"a newer API's addition"}"""),
            Event("done", DoneJson)));

        var events = await ToListAsync(api.StreamAskAsync(WorldId, "Q"));

        Assert.That(events.Select(e => e.GetType()), Is.EqualTo([typeof(AskStreamEvent.Started), typeof(AskStreamEvent.Done)]));
    }

    [Test]
    public async Task StreamPublicAsk_PostsToThePublicWorld()
    {
        HttpRequestMessage? sent = null;
        var api = Client(request =>
        {
            sent = request;
            return Sse(Event("done", DoneJson));
        });

        await ToListAsync(api.StreamPublicAskAsync("black harbor", "Q"));

        Assert.That(sent!.RequestUri!.AbsolutePath, Is.EqualTo("/api/public/worlds/black%20harbor/ask"));
        Assert.That(sent.Method, Is.EqualTo(HttpMethod.Post));
    }

    #endregion

    #region StreamingAnswer

    [Test]
    public void Apply_RendersTheFirstWordAtOnce_ThenCoalescesDeltasWithinTheInterval()
    {
        var clock = new ManualClock();
        var answer = new StreamingAnswer(clock);

        Assert.Multiple(() =>
        {
            Assert.That(answer.Apply(new AskStreamEvent.Started("High", [])), Is.True, "confidence shows at once");
            Assert.That(answer.Apply(new AskStreamEvent.Delta("The ")), Is.True, "the first words render at once");
            clock.Advance(TimeSpan.FromMilliseconds(10));
            Assert.That(answer.Apply(new AskStreamEvent.Delta("harbor ")), Is.False, "coalesced");
            clock.Advance(StreamingAnswer.RenderInterval);
            Assert.That(answer.Apply(new AskStreamEvent.Delta("master ")), Is.True, "the interval has passed");
            Assert.That(answer.Text, Is.EqualTo("The harbor master"), "coalesced text is not lost");
        });
    }

    [Test]
    public void Apply_Failure_DropsThePartialText()
    {
        var answer = new StreamingAnswer(new ManualClock());
        answer.Apply(new AskStreamEvent.Delta("Captain Voss is "));

        answer.Apply(new AskStreamEvent.Failed(new ApiError("service_unavailable", "Unavailable.")));

        Assert.Multiple(() =>
        {
            Assert.That(answer.HasText, Is.False, "a truncated answer is not left on screen as if whole");
            Assert.That(answer.Error!.Code, Is.EqualTo("service_unavailable"));
        });
    }

    [Test]
    public async Task ReadAsync_StopsReading_WhenTheAnswerIsNoLongerWanted()
    {
        var answer = new StreamingAnswer(new ManualClock());
        var produced = 0;
        var wanted = true;

        async IAsyncEnumerable<AskStreamEvent> Events()
        {
            for (var i = 0; i < 100; i++)
            {
                produced++;
                if (i == 2)
                {
                    wanted = false; // e.g. the world switched mid-answer
                }

                await Task.Yield();
                yield return new AskStreamEvent.Delta("word ");
            }
        }

        var finished = await answer.ReadAsync(Events(), () => wanted, () => { });

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.False);
            Assert.That(produced, Is.LessThan(5), "reading stopped, which is what aborts the request upstream");
        });
    }

    [Test]
    public async Task ReadAsync_ACancelledToken_EndsQuietly()
    {
        var answer = new StreamingAnswer(new ManualClock());
        using var cts = new CancellationTokenSource();

        async IAsyncEnumerable<AskStreamEvent> Events()
        {
            yield return new AskStreamEvent.Delta("word ");
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
            yield return new AskStreamEvent.Delta("never");
        }

        Assert.That(await answer.ReadAsync(Events(), () => true, () => { }), Is.False,
            "a component that went away is not an error to show anyone");
    }

    [Test]
    public async Task ReadAsync_RunsToDone_AndReportsFinished()
    {
        var answer = new StreamingAnswer(new ManualClock());
        var done = new AskAnswer("Voss.", [], "High", []);
        var renders = 0;

        var finished = await answer.ReadAsync(
            AsAsync(new AskStreamEvent.Started("High", []), new AskStreamEvent.Delta("Voss."), new AskStreamEvent.Done(done)),
            () => true, () => renders++);

        Assert.Multiple(() =>
        {
            Assert.That(finished, Is.True);
            Assert.That(answer.Answer, Is.SameAs(done));
            Assert.That(renders, Is.EqualTo(3));
        });
    }

    #endregion

    #region LoremasterDisplay

    [TestCase("Captain Voss [ref:art-1] runs it. [ref:fact:9]", "Captain Voss runs it.")]
    [TestCase("Captain Voss runs it [ref:fact:12", "Captain Voss runs it")]
    [TestCase("Captain Voss runs it [ref:", "Captain Voss runs it")]
    [TestCase("Captain Voss runs it [re", "Captain Voss runs it")]
    [TestCase("Captain Voss runs it [", "Captain Voss runs it")]
    [TestCase("Rooms [2] and [3] are sealed", "Rooms [2] and [3] are sealed")]
    public void CleanAnswer_StripsMarkers_IncludingOneStillBeingWritten(string raw, string expected)
    {
        Assert.That(LoremasterDisplay.CleanAnswer(raw), Is.EqualTo(expected));
    }

    [Test]
    public void ToExchange_CarriesTheCleanedAnswerAndItsCitations()
    {
        var citation = new Citation("art-1", "Artifact", "Captain Voss", Guid.NewGuid(), null, null, null, null, "captain-voss");
        var exchange = LoremasterDisplay.ToExchange("Who?", new AskAnswer("Voss. [ref:art-1]", [citation], "High", ["Rumor"]));

        Assert.Multiple(() =>
        {
            Assert.That(exchange.Question, Is.EqualTo("Who?"));
            Assert.That(exchange.Answer, Is.EqualTo("Voss."));
            Assert.That(exchange.Citations.Single().Slug, Is.EqualTo("captain-voss"));
            Assert.That(exchange.Caveats, Is.EqualTo(["Rumor"]));
        });
    }

    #endregion

    #region Helpers

    private static NornisApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("http://localhost") },
            new ViewAsState(), new ActivitySignal(), new AuthSessionState());

    private static string Event(string name, string json) => $"event: {name}\ndata: {json.Trim()}\n\n";

    private static HttpResponseMessage Sse(params string[] events)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Concat(events), Encoding.UTF8),
        };
        response.Content.Headers.ContentType = new("text/event-stream");
        return response;
    }

    private static async Task WriteAsync(Pipe pipe, string text)
    {
        await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes(text));
        await pipe.Writer.FlushAsync();
    }

    private static async Task<List<AskStreamEvent>> ToListAsync(IAsyncEnumerable<AskStreamEvent> events)
    {
        var list = new List<AskStreamEvent>();
        await foreach (var e in events)
        {
            list.Add(e);
        }

        return list;
    }

    private static async IAsyncEnumerable<AskStreamEvent> AsAsync(params AskStreamEvent[] events)
    {
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => _ticks;

        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }

    #endregion
}
