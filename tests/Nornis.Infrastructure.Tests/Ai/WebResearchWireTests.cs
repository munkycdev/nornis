using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Web research (feature 40) per provider, through the real SDKs and their adapters against a
/// fake provider, and through <see cref="AiCallExecutor.StreamAsync"/>, which is how Prep's side
/// conversation calls: the request carries the provider's search tool and nothing else, a turn
/// without the switch carries no tool, the stream's pages and searches come back on the last
/// chunk, and a model that refuses the tool fails as <see cref="AiWebSearchUnsupportedException"/>.
///
/// Feature 39's lesson is why these are per provider: each one's limits and wire shape differ,
/// and a test against one says nothing about the others. The streams are shaped from each
/// provider's documentation (2026-11-06); <c>LiveWebResearchTests</c> checks them for real.
/// </summary>
[TestFixture]
public class WebResearchWireTests
{
    private static readonly AiCallOptions Research = new(MaxOutputTokens: 6_000, WebSearch: new AiWebSearch(MaxSearches: 5));
    private static readonly AiCallOptions Plain = new(MaxOutputTokens: 2_500);

    // ------------------------------------------------------------------ Claude --

    private const string ClaudeModel = "claude-sonnet-5-5";

    private const string ClaudeStream = """
        event: message_start
        data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":100,"output_tokens":1}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"server_tool_use","id":"srvtoolu_1","name":"web_search","input":{}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"query\":\"Waterdeep wards\"}"}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":0}

        event: content_block_start
        data: {"type":"content_block_start","index":1,"content_block":{"type":"web_search_tool_result","tool_use_id":"srvtoolu_1","content":[{"type":"web_search_result","url":"https://example.org/waterdeep","title":"Waterdeep, City of Splendors","encrypted_content":"abc","page_age":"2025"},{"type":"web_search_result","url":"https://example.org/uncited","title":"Read but not cited","encrypted_content":"def","page_age":"2024"}]}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":1}

        event: content_block_start
        data: {"type":"content_block_start","index":2,"content_block":{"type":"text","text":"","citations":[]}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":2,"delta":{"type":"citations_delta","citation":{"type":"web_search_result_location","url":"https://example.org/waterdeep","title":"Waterdeep, City of Splendors","encrypted_index":"x","cited_text":"The city is divided into wards."}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":2,"delta":{"type":"text_delta","text":"Waterdeep is divided into wards."}}

        event: content_block_stop
        data: {"type":"content_block_stop","index":2}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"input_tokens":5000,"output_tokens":40,"server_tool_use":{"web_search_requests":2,"web_fetch_requests":0}}}

        event: message_stop
        data: {"type":"message_stop"}


        """;

    [Test]
    public async Task Claude_research_sends_only_the_basic_search_tool_capped_at_the_turns_searches()
    {
        var handler = new RecordingHandler(_ => Sse(ClaudeStream));
        await DrainAsync(Claude(handler), Research);

        var body = handler.Bodies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(body["tools"], JsonNode.Parse("""[{"name":"web_search","type":"web_search_20250305","max_uses":5}]""")),
                Is.True, body.ToJsonString());
            Assert.That(body["max_tokens"]!.GetValue<int>(), Is.EqualTo(6_000));
        });
    }

    [Test]
    public async Task Claude_without_the_switch_sends_no_tool()
    {
        var handler = new RecordingHandler(_ => Sse(ClaudeStream));
        await DrainAsync(Claude(handler), Plain);

        Assert.That(handler.Bodies.Single()["tools"], Is.Null, handler.Bodies.Single().ToJsonString());
    }

    [Test]
    public async Task Claude_reports_the_cited_pages_and_the_searches_it_ran()
    {
        var chunks = await DrainAsync(Claude(new RecordingHandler(_ => Sse(ClaudeStream))), Research);

        var last = chunks[^1];
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.EqualTo("Waterdeep is divided into wards."));
            Assert.That(last.WebResearch?.Pages.Select(p => (p.Title, p.Url)),
                Is.EqualTo([("Waterdeep, City of Splendors", "https://example.org/waterdeep")]),
                "a page the model read but didn't cite isn't one the answer drew on");
            Assert.That(last.WebResearch?.Searches, Is.EqualTo(2));
            Assert.That(last.Usage?.WebSearches, Is.EqualTo(2), "the ledger prices what usage says");
            Assert.That(last.WebResearch?.SuggestionsHtml, Is.Null);
        });
    }

    [Test]
    public async Task Claude_without_the_switch_reports_no_research()
    {
        var chunks = await DrainAsync(Claude(new RecordingHandler(_ => Sse(ClaudeStream))), Plain);

        Assert.Multiple(() =>
        {
            Assert.That(chunks[^1].WebResearch, Is.Null);
            Assert.That(chunks[^1].Usage?.WebSearches, Is.Null);
        });
    }

    [Test]
    public async Task Claude_refusing_the_tool_fails_as_cant_search_and_other_refusals_dont()
    {
        const string refused = """{"type":"error","error":{"type":"invalid_request_error","message":"tools.0: web_search_20250305 is not supported on this model."}}""";
        const string other = """{"type":"error","error":{"type":"invalid_request_error","message":"messages: text content blocks must be non-empty"}}""";

        var cantSearch = await Assert.CatchAsync(() => DrainAsync(Claude(Failing(refused)), Research));
        var otherFailure = await Assert.CatchAsync(() => DrainAsync(Claude(Failing(other)), Research));
        var notSearching = await Assert.CatchAsync(() => DrainAsync(Claude(Failing(refused)), Plain));

        Assert.Multiple(() =>
        {
            Assert.That(cantSearch, Is.InstanceOf<AiWebSearchUnsupportedException>());
            Assert.That(otherFailure, Is.Not.InstanceOf<AiWebSearchUnsupportedException>().And.InstanceOf<AiHttpException>());
            Assert.That(notSearching, Is.Not.InstanceOf<AiWebSearchUnsupportedException>(),
                "only a call that asked to search can be refused the search tool");
        });
    }

    // ------------------------------------------------------------------ Gemini --

    private const string GeminiModel = "gemini-3.8-flash";

    private const string GeminiStream =
        "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"Waterdeep is divided \"}]},\"index\":0}],"
        + "\"usageMetadata\":{\"promptTokenCount\":100,\"totalTokenCount\":100},\"modelVersion\":\"gemini-3.8-flash\",\"responseId\":\"r1\"}\r\n\r\n"
        + "data: {\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":\"into wards.\"}]},\"finishReason\":\"STOP\",\"index\":0,"
        + "\"groundingMetadata\":{\"webSearchQueries\":[\"waterdeep wards\",\"waterdeep map\"],"
        + "\"searchEntryPoint\":{\"renderedContent\":\"<div class=\\\"chip\\\">waterdeep wards</div>\"},"
        + "\"groundingChunks\":[{\"web\":{\"uri\":\"https://vertexaisearch.cloud.google.com/grounding-api-redirect/abc\",\"title\":\"example.org\"}}],"
        + "\"groundingSupports\":[{\"segment\":{\"startIndex\":0,\"endIndex\":9,\"text\":\"Waterdeep\"},\"groundingChunkIndices\":[0]}]}}],"
        + "\"usageMetadata\":{\"promptTokenCount\":100,\"candidatesTokenCount\":20,\"totalTokenCount\":120},\"modelVersion\":\"gemini-3.8-flash\",\"responseId\":\"r1\"}\r\n\r\n";

    [Test]
    public async Task Gemini_research_sends_only_google_search_grounding()
    {
        var handler = new RecordingHandler(_ => Sse(GeminiStream));
        await DrainAsync(Gemini(handler), Research);

        var body = handler.Bodies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(JsonNode.DeepEquals(body["tools"], JsonNode.Parse("""[{"googleSearch":{}}]""")), Is.True, body.ToJsonString());
            Assert.That(body["generationConfig"]?["maxOutputTokens"]?.GetValue<int>(), Is.EqualTo(6_000));
        });
    }

    [Test]
    public async Task Gemini_without_the_switch_sends_no_tool()
    {
        var handler = new RecordingHandler(_ => Sse(GeminiStream));
        await DrainAsync(Gemini(handler), Plain);

        Assert.That(handler.Bodies.Single()["tools"], Is.Null, handler.Bodies.Single().ToJsonString());
    }

    [Test]
    public async Task Gemini_reports_its_grounding_pages_queries_and_search_suggestions()
    {
        var chunks = await DrainAsync(Gemini(new RecordingHandler(_ => Sse(GeminiStream))), Research);

        var last = chunks[^1];
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.EqualTo("Waterdeep is divided into wards."));
            Assert.That(last.WebResearch?.Pages.Select(p => (p.Title, p.Url)), Is.EqualTo(
            [
                ("example.org", "https://vertexaisearch.cloud.google.com/grounding-api-redirect/abc"),
            ]));
            Assert.That(last.WebResearch?.Searches, Is.EqualTo(2), "Gemini 3 bills each query it runs");
            Assert.That(last.Usage?.WebSearches, Is.EqualTo(2));
            Assert.That(last.WebResearch?.SuggestionsHtml, Is.EqualTo("<div class=\"chip\">waterdeep wards</div>"));
        });
    }

    [Test]
    public async Task Gemini_refusing_grounding_fails_as_cant_search()
    {
        const string refused = """{"error":{"code":400,"message":"Search Grounding is not supported for this model.","status":"INVALID_ARGUMENT"}}""";

        var thrown = await Assert.CatchAsync(() =>
            DrainAsync(Gemini(new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, refused))), Research));

        Assert.That(thrown, Is.InstanceOf<AiWebSearchUnsupportedException>());
    }

    // ------------------------------------------------------------------ OpenAI --

    private const string OpenAiModel = "gpt-5.4";

    private const string OpenAiAnswer =
        """{"type":"output_text","text":"Waterdeep is divided into wards.","annotations":[{"type":"url_citation","start_index":0,"end_index":9,"url":"https://example.org/waterdeep?utm_source=openai","title":"Waterdeep"}]}""";

    /// <summary>
    /// A Responses stream for one search: the <c>web_search_call</c> item is reported as it starts
    /// and again when done, which is why searches are counted by call id.
    /// </summary>
    private static readonly string OpenAiStream = ResponsesSse(
        "{\"type\":\"response.created\",\"sequence_number\":0,\"response\":" + OpenAiResponse("in_progress", "[]", usage: null) + "}",
        """{"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"type":"web_search_call","id":"ws_1","status":"in_progress"}}""",
        """{"type":"response.web_search_call.in_progress","sequence_number":2,"output_index":0,"item_id":"ws_1"}""",
        """{"type":"response.web_search_call.searching","sequence_number":3,"output_index":0,"item_id":"ws_1"}""",
        """{"type":"response.web_search_call.completed","sequence_number":4,"output_index":0,"item_id":"ws_1"}""",
        """{"type":"response.output_item.done","sequence_number":5,"output_index":0,"item":{"type":"web_search_call","id":"ws_1","status":"completed","action":{"type":"search","query":"waterdeep wards"}}}""",
        """{"type":"response.output_item.added","sequence_number":6,"output_index":1,"item":{"type":"message","id":"msg_1","status":"in_progress","role":"assistant","content":[]}}""",
        """{"type":"response.content_part.added","sequence_number":7,"output_index":1,"item_id":"msg_1","content_index":0,"part":{"type":"output_text","text":"","annotations":[]}}""",
        """{"type":"response.output_text.delta","sequence_number":8,"output_index":1,"item_id":"msg_1","content_index":0,"delta":"Waterdeep is divided into wards."}""",
        """{"type":"response.output_text.annotation.added","sequence_number":9,"output_index":1,"item_id":"msg_1","content_index":0,"annotation_index":0,"annotation":{"type":"url_citation","start_index":0,"end_index":9,"url":"https://example.org/waterdeep?utm_source=openai","title":"Waterdeep"}}""",
        """{"type":"response.output_text.done","sequence_number":10,"output_index":1,"item_id":"msg_1","content_index":0,"text":"Waterdeep is divided into wards."}""",
        "{\"type\":\"response.content_part.done\",\"sequence_number\":11,\"output_index\":1,\"item_id\":\"msg_1\",\"content_index\":0,\"part\":" + OpenAiAnswer + "}",
        "{\"type\":\"response.output_item.done\",\"sequence_number\":12,\"output_index\":1,\"item\":{\"type\":\"message\",\"id\":\"msg_1\",\"status\":\"completed\",\"role\":\"assistant\",\"content\":[" + OpenAiAnswer + "]}}",
        "{\"type\":\"response.completed\",\"sequence_number\":13,\"response\":" + OpenAiResponse(
            "completed",
            """[{"type":"web_search_call","id":"ws_1","status":"completed","action":{"type":"search","query":"waterdeep wards"}},{"type":"message","id":"msg_1","status":"completed","role":"assistant","content":[""" + OpenAiAnswer + "]}]",
            usage: """{"input_tokens":5000,"input_tokens_details":{"cached_tokens":0},"output_tokens":40,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":5040}""") + "}");

    /// <summary>A research turn where the model answered without searching.</summary>
    private static readonly string OpenAiStreamWithoutSearching = ResponsesSse(
        "{\"type\":\"response.created\",\"sequence_number\":0,\"response\":" + OpenAiResponse("in_progress", "[]", usage: null) + "}",
        """{"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":{"type":"message","id":"msg_1","status":"in_progress","role":"assistant","content":[]}}""",
        """{"type":"response.output_text.delta","sequence_number":2,"output_index":0,"item_id":"msg_1","content_index":0,"delta":"The record already says."}""",
        "{\"type\":\"response.completed\",\"sequence_number\":3,\"response\":" + OpenAiResponse(
            "completed",
            """[{"type":"message","id":"msg_1","status":"completed","role":"assistant","content":[{"type":"output_text","text":"The record already says.","annotations":[]}]}]""",
            usage: """{"input_tokens":5000,"input_tokens_details":{"cached_tokens":0},"output_tokens":10,"output_tokens_details":{"reasoning_tokens":0},"total_tokens":5010}""") + "}");

    private const string ChatCompletionsStream =
        "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"gpt-5.4\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"Wards.\"},\"finish_reason\":null}]}\n\n"
        + "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"gpt-5.4\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n"
        + "data: {\"id\":\"c1\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"gpt-5.4\",\"choices\":[],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":2,\"total_tokens\":102}}\n\n"
        + "data: [DONE]\n\n";

    [Test]
    public async Task OpenAi_research_goes_to_responses_with_only_the_search_tool_capped_and_not_stored()
    {
        var handler = new RecordingHandler(_ => Sse(OpenAiStream));
        await DrainAsync(OpenAi(handler, AiClientShape.Responses), Research);

        var body = handler.Bodies.Single();
        Assert.Multiple(() =>
        {
            Assert.That(handler.Uris.Single().AbsolutePath, Is.EqualTo("/v1/responses"));
            Assert.That(JsonNode.DeepEquals(body["tools"], JsonNode.Parse("""[{"type":"web_search"}]""")), Is.True, body.ToJsonString());
            Assert.That(body["max_tool_calls"]?.GetValue<int>(), Is.EqualTo(5));
            Assert.That(body["max_output_tokens"]?.GetValue<int>(), Is.EqualTo(6_000));
            Assert.That(body["store"]?.GetValue<bool>(), Is.False, "a Prep prompt carries Hidden truth; OpenAI keeps stored responses for 30 days");
        });
    }

    [Test]
    public async Task OpenAi_without_the_switch_stays_on_chat_completions_as_before()
    {
        var handler = new RecordingHandler(_ => Sse(ChatCompletionsStream));
        var chunks = await DrainAsync(OpenAi(handler, AiClientShape.Chat), Plain);

        // Today's body, pinned: a research-free turn must not change by a byte.
        var expected = JsonNode.Parse("""
            {"messages":[{"role":"system","content":"You are the Loremaster."},{"role":"user","content":"What are Waterdeep's wards?"}],
             "model":"gpt-5.4","max_completion_tokens":2500,"stream":true,"stream_options":{"include_usage":true}}
            """);
        Assert.Multiple(() =>
        {
            Assert.That(handler.Uris.Single().AbsolutePath, Is.EqualTo("/v1/chat/completions"));
            Assert.That(JsonNode.DeepEquals(handler.Bodies.Single(), expected), Is.True, handler.Bodies.Single().ToJsonString());
            Assert.That(chunks[^1].WebResearch, Is.Null);
        });
    }

    [Test]
    public async Task OpenAi_reports_the_cited_pages_and_counts_each_search_once()
    {
        var chunks = await DrainAsync(OpenAi(new RecordingHandler(_ => Sse(OpenAiStream)), AiClientShape.Responses), Research);

        var last = chunks[^1];
        Assert.Multiple(() =>
        {
            Assert.That(string.Concat(chunks.Select(c => c.Text)), Is.EqualTo("Waterdeep is divided into wards."));
            Assert.That(last.WebResearch?.Pages.Select(p => (p.Title, p.Url)),
                Is.EqualTo([("Waterdeep", "https://example.org/waterdeep?utm_source=openai")]));
            Assert.That(last.WebResearch?.Searches, Is.EqualTo(1), "the call is reported as it starts and when done");
            Assert.That(last.Usage?.WebSearches, Is.EqualTo(1));
            Assert.That(last.Usage?.InputTokens, Is.EqualTo(5_000));
        });
    }

    [Test]
    public async Task OpenAi_research_that_didnt_search_reports_zero_searches()
    {
        var chunks = await DrainAsync(OpenAi(new RecordingHandler(_ => Sse(OpenAiStreamWithoutSearching)), AiClientShape.Responses), Research);

        Assert.Multiple(() =>
        {
            Assert.That(chunks[^1].WebResearch?.Searches, Is.Zero, "every search is an item of the response, so none is a count");
            Assert.That(chunks[^1].WebResearch?.Pages, Is.Empty);
        });
    }

    [Test]
    public async Task OpenAi_refusing_the_tool_fails_as_cant_search()
    {
        const string refused = """{"error":{"message":"Tool 'web_search' is not supported with gpt-5.4-nano.","type":"invalid_request_error","param":"tools","code":null}}""";

        var thrown = await Assert.CatchAsync(() =>
            DrainAsync(OpenAi(new RecordingHandler(_ => Json(HttpStatusCode.BadRequest, refused)), AiClientShape.Responses), Research));

        Assert.That(thrown, Is.InstanceOf<AiWebSearchUnsupportedException>());
    }

    [Test]
    public void OpenAi_search_options_never_take_a_schema() =>
        Assert.Throws<NotSupportedException>(() => OpenAiProviderTraits.Instance.ChatOptions(
            Research with { Schema = new AiJsonSchema("x", """{"type":"object"}""") }));

    // ------------------------------------------------------------------ plumbing --

    private static (IChatClient Client, IAiProviderTraits Traits, AiFunding Funding) Claude(HttpMessageHandler handler) =>
        (new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler).For(KeyFor(AiProvider.Anthropic), ClaudeModel, () => "sk-ant-test"),
            AnthropicProviderTraits.Instance, new AiFunding(AiProvider.Anthropic, ClaudeModel, AiFundedBy.Key, AiKeyId: Guid.NewGuid()));

    private static (IChatClient Client, IAiProviderTraits Traits, AiFunding Funding) Gemini(HttpMessageHandler handler) =>
        (new UserKeyChatClients(maxRetries: 0, geminiHandler: handler).For(KeyFor(AiProvider.Gemini), GeminiModel, () => "AIza-test"),
            GeminiProviderTraits.Instance, new AiFunding(AiProvider.Gemini, GeminiModel, AiFundedBy.Key, AiKeyId: Guid.NewGuid()));

    private static (IChatClient Client, IAiProviderTraits Traits, AiFunding Funding) OpenAi(HttpMessageHandler handler, AiClientShape shape) =>
        (new UserKeyChatClients(maxRetries: 0, openAiHandler: handler).For(KeyFor(AiProvider.OpenAi), OpenAiModel, () => "sk-test", shape),
            OpenAiProviderTraits.Instance, new AiFunding(AiProvider.OpenAi, OpenAiModel, AiFundedBy.Key, AiKeyId: Guid.NewGuid()));

    private static string OpenAiResponse(string status, string output, string? usage) =>
        $$"""{"id":"resp_1","object":"response","created_at":1,"status":"{{status}}","model":"gpt-5.4","parallel_tool_calls":true,"tool_choice":"auto","tools":[],"output":{{output}}{{(usage is null ? "" : ",\"usage\":" + usage)}}}""";

    private static string ResponsesSse(params string[] events) =>
        string.Concat(events.Select(e => $"event: {JsonNode.Parse(e)!["type"]}\ndata: {e}\n\n"));

    private static UserAiKey KeyFor(AiProvider provider) => new() { Id = Guid.NewGuid(), Provider = provider, UpdatedAt = DateTimeOffset.UnixEpoch };

    private static async Task<List<AiStreamChunk>> DrainAsync(
        (IChatClient Client, IAiProviderTraits Traits, AiFunding Funding) provider, AiCallOptions options)
    {
        var binder = new OneBinder(new AiBinding(provider.Client, provider.Traits, provider.Funding, DateTimeOffset.UnixEpoch));
        var request = new AiPromptRequest
        {
            Scope = AiCallScope.ForWorld(Guid.NewGuid()),
            SystemPrompt = "You are the Loremaster.",
            UserMessage = "What are Waterdeep's wards?",
            Model = "nornis-ask",
            TimeoutSeconds = 30,
        };

        var chunks = new List<AiStreamChunk>();
        await foreach (var chunk in AiCallExecutor.StreamAsync(binder, AiTask.Prep, request, options, "Test", NullLogger.Instance, CancellationToken.None))
        {
            chunks.Add(chunk);
        }

        return chunks;
    }

    private static RecordingHandler Failing(string body) => new(_ => Json(HttpStatusCode.BadRequest, body));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// An event stream as the network delivers one: read once, front to back, and not a
    /// MemoryStream. StringContent's stream is one, and System.ClientModel buffers a MemoryStream
    /// body when the response is disposed — which throws once the SDK has read it to the end. With
    /// the SDK's own network timeout on, its read-timeout wrapper hid that; the clients now leave
    /// the timeout to the executor (see AzureOpenAiClientFactory.Options), so the test stream has
    /// to look like the real one.
    /// </summary>
    private static HttpResponseMessage Sse(string events)
    {
        var bytes = Encoding.UTF8.GetBytes(events.Replace("\r\n", "\n"));
        var content = new StreamContent(new NetworkStreamStandIn(bytes));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") { CharSet = "utf-8" };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class NetworkStreamStandIn(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class OneBinder(AiBinding binding) : IAiCallBinder
    {
        public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, bool needsWebSearch, CancellationToken ct) =>
            Task.FromResult(binding);

        public Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<JsonNode> Bodies { get; } = [];

        public List<Uri> Uris { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!);
            if (request.Content is not null)
            {
                Bodies.Add(JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!);
            }

            return respond(request);
        }
    }
}
