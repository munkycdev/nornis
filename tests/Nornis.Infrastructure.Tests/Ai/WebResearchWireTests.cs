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

    [Test]
    public void OpenAi_doesnt_search_on_chat_completions() =>
        Assert.Throws<NotSupportedException>(() => OpenAiProviderTraits.Instance.ChatOptions(Research),
            "OpenAI's search is a Responses tool, which feature 40 Phase B adds");

    // ------------------------------------------------------------------ plumbing --

    private static (IChatClient Client, IAiProviderTraits Traits, AiFunding Funding) Claude(HttpMessageHandler handler) =>
        (new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler).For(KeyFor(AiProvider.Anthropic), ClaudeModel, () => "sk-ant-test"),
            AnthropicProviderTraits.Instance, new AiFunding(AiProvider.Anthropic, ClaudeModel, AiFundedBy.Key, AiKeyId: Guid.NewGuid()));

    private static (IChatClient Client, IAiProviderTraits Traits, AiFunding Funding) Gemini(HttpMessageHandler handler) =>
        (new UserKeyChatClients(maxRetries: 0, geminiHandler: handler).For(KeyFor(AiProvider.Gemini), GeminiModel, () => "AIza-test"),
            GeminiProviderTraits.Instance, new AiFunding(AiProvider.Gemini, GeminiModel, AiFundedBy.Key, AiKeyId: Guid.NewGuid()));

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

    private static HttpResponseMessage Sse(string events) =>
        new(HttpStatusCode.OK) { Content = new StringContent(events.Replace("\r\n", "\n"), Encoding.UTF8, "text/event-stream") };

    private sealed class OneBinder(AiBinding binding) : IAiCallBinder
    {
        public Task<AiBinding> BindAsync(AiCallScope scope, AiTask task, string nornisModel, bool needsImages, CancellationToken ct) =>
            Task.FromResult(binding);

        public Task ReportAsync(AiBinding binding, Exception? failure, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<JsonNode> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null)
            {
                Bodies.Add(JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!);
            }

            return respond(request);
        }
    }
}
