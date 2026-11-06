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
/// Claude keys (feature 39 Phase C), through the real SDK and its <see cref="IChatClient"/>
/// adapter against a fake Anthropic: what reaches the wire, and what each failure means.
/// </summary>
[TestFixture]
public class AnthropicProviderTraitsTests
{
    private const string Secret = "sk-ant-redaction-canary-1234";
    private const string Model = "claude-sonnet-5-5";

    private static readonly AnthropicProviderTraits Traits = AnthropicProviderTraits.Instance;

    private static readonly AiFunding KeyFunded = new(AiProvider.Anthropic, Model, AiFundedBy.Key, AiKeyId: Guid.NewGuid());

    private static readonly UserAiKey Key = new()
    {
        Id = Guid.NewGuid(),
        Provider = AiProvider.Anthropic,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Test]
    public void ChatOptions_NothingAsked_SendsNoOptionsAtAll()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Traits.ChatOptions(null), Is.Null);
            Assert.That(Traits.ChatOptions(new AiCallOptions()), Is.Null);
        });
    }

    [Test]
    public async Task A_schema_and_a_ceiling_reach_the_wire_as_output_config_and_max_tokens()
    {
        const string schema = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""";
        var handler = new RecordingHandler(_ => Message("""{"name":"Voss"}"""));
        using var clients = new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler);

        var response = await clients.For(Key, Model, () => Secret).GetResponseAsync(
            [new ChatMessage(ChatRole.System, "You are the Loremaster."), new ChatMessage(ChatRole.User, "Who sank the Gull?")],
            Traits.ChatOptions(new AiCallOptions(new AiJsonSchema("who", schema), MaxOutputTokens: 2_500)));

        var body = handler.Bodies.Single();
        var format = body["output_config"]?["format"];
        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.EqualTo("""{"name":"Voss"}"""));
            Assert.That(handler.Requests.Single().RequestUri, Is.EqualTo(new Uri("https://api.anthropic.com/v1/messages")));
            Assert.That(handler.ApiKeys.Single(), Is.EqualTo(Secret));
            Assert.That(body["model"]!.GetValue<string>(), Is.EqualTo(Model));
            Assert.That(body["max_tokens"]!.GetValue<int>(), Is.EqualTo(2_500));
            Assert.That(format?["type"]?.GetValue<string>(), Is.EqualTo("json_schema"), body.ToJsonString());
            Assert.That(JsonNode.DeepEquals(format?["schema"], JsonNode.Parse(schema)), Is.True, body.ToJsonString());
        });
    }

    [Test]
    public async Task The_extraction_schema_reaches_the_wire_within_Anthropics_limits()
    {
        // Production 2026-11-03 to 11-06: every extraction on a Claude key was refused with
        // "Schemas contains too many parameters with union types (18 ...) limit: 16". Checked
        // here on the body the real SDK adapter sends, not on what the traits hand it.
        var handler = new RecordingHandler(_ => Message("""{"proposals":[]}"""));
        using var clients = new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler);

        await clients.For(Key, Model, () => Secret).GetResponseAsync(
            "We questioned Captain Voss.", Traits.ChatOptions(ExtractionAiClient.CallOptions));

        var sent = handler.Bodies.Single()["output_config"]?["format"]?["schema"];
        Assert.That(sent, Is.Not.Null, handler.Bodies.Single().ToJsonString());
        Assert.That(AnthropicSchemaLimits.Count(sent!).WithinLimits, Is.True, sent!.ToJsonString());
    }

    [Test]
    public async Task A_call_that_names_no_ceiling_gets_the_clients_default()
    {
        var handler = new RecordingHandler(_ => Message("Voss"));
        using var clients = new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler);

        await clients.For(Key, Model, () => Secret).GetResponseAsync("Who sank the Gull?", Traits.ChatOptions(null));

        Assert.That(handler.Bodies.Single()["max_tokens"]!.GetValue<int>(), Is.EqualTo(UserKeyChatClients.AnthropicDefaultMaxTokens));
    }

    [Test]
    public async Task The_system_prompt_is_marked_as_a_cache_breakpoint_on_the_wire()
    {
        var handler = new RecordingHandler(_ => Message("Voss"));
        using var clients = new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler);

        await clients.For(Key, Model, () => Secret).GetResponseAsync(
            [new ChatMessage(ChatRole.System, "You are the Loremaster."), new ChatMessage(ChatRole.User, "Who sank the Gull?")]);

        var system = handler.Bodies.Single()["system"]!.AsArray();
        Assert.That(system[^1]!["cache_control"]?["type"]?.GetValue<string>(), Is.EqualTo("ephemeral"),
            handler.Bodies.Single().ToJsonString());
    }

    [Test]
    public void The_breakpoint_leaves_the_callers_messages_alone()
    {
        var system = new ChatMessage(ChatRole.System, "You are the Loremaster.") { AuthorName = "nornis" };
        var user = new ChatMessage(ChatRole.User, "Who sank the Gull?");
        List<ChatMessage> messages = [system, user];

        var marked = AnthropicPromptCaching.WithBreakpoint(messages);

        Assert.Multiple(() =>
        {
            Assert.That(messages, Is.EqualTo([system, user]));
            Assert.That(system.Contents.Single().AdditionalProperties, Is.Null.Or.Empty);
            Assert.That(marked[0], Is.Not.SameAs(system));
            Assert.That(marked[0].AuthorName, Is.EqualTo("nornis"));
            Assert.That(marked[0].Text, Is.EqualTo("You are the Loremaster."));
            Assert.That(marked[1], Is.SameAs(user));
        });
    }

    [Test]
    public void Without_a_system_message_nothing_is_marked()
    {
        var user = new ChatMessage(ChatRole.User, "Who sank the Gull?");

        Assert.That(AnthropicPromptCaching.WithBreakpoint([user]), Is.EqualTo([user]));
    }

    [TestCase(HttpStatusCode.Unauthorized, """{"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key"}}""")]
    [TestCase(HttpStatusCode.Forbidden, """{"type":"error","error":{"type":"permission_error","message":"not allowed"}}""")]
    public async Task A_refused_key_is_rejected(HttpStatusCode status, string body)
    {
        var translated = await FailureFor(status, body);

        Assert.That(translated, Is.InstanceOf<AiKeyRejectedException>());
    }

    [TestCase(HttpStatusCode.PaymentRequired, """{"type":"error","error":{"type":"billing_error","message":"billing issue"}}""")]
    [TestCase(HttpStatusCode.BadRequest, """{"type":"error","error":{"type":"invalid_request_error","message":"Your credit balance is too low to access the Anthropic API."}}""")]
    public async Task An_empty_account_is_out_of_credit(HttpStatusCode status, string body)
    {
        var translated = await FailureFor(status, body);

        Assert.That(translated, Is.InstanceOf<AiCreditExhaustedException>());
    }

    [TestCase(HttpStatusCode.BadRequest)]
    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase((HttpStatusCode)529)]
    [TestCase(HttpStatusCode.InternalServerError)]
    public async Task Any_other_status_is_kept_so_the_retry_rules_see_it(HttpStatusCode status)
    {
        var translated = await FailureFor(status, """{"type":"error","error":{"type":"some_error","message":"no"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(translated, Is.Not.InstanceOf<AiRefusalException>());
            Assert.That(translated?.StatusCode, Is.EqualTo((int)status));
        });
    }

    [Test]
    public async Task An_unreachable_provider_has_no_status_so_it_is_retried()
    {
        var translated = await FailureFor(_ => throw new HttpRequestException("no route"));

        Assert.Multiple(() =>
        {
            Assert.That(translated, Is.Not.Null);
            Assert.That(translated!.StatusCode, Is.Null);
        });
    }

    [Test]
    public async Task Overloaded_inside_the_stream_reads_as_a_529()
    {
        var handler = new RecordingHandler(_ => Stream("""
            event: message_start
            data: {"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-5","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":10,"output_tokens":1}}}

            event: error
            data: {"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}


            """));
        using var clients = new UserKeyChatClients(maxRetries: 0, anthropicHandler: handler);

        var thrown = Assert.CatchAsync(async () =>
        {
            await foreach (var _ in clients.For(Key, Model, () => Secret).GetStreamingResponseAsync("Who sank the Gull?"))
            {
            }
        });

        var translated = Traits.TranslateFailure(thrown!, KeyFunded, "Test", NullLogger.Instance);
        Assert.That(translated?.StatusCode, Is.EqualTo(529), thrown!.ToString());
    }

    [Test]
    public void Something_else_is_not_this_providers_to_name() =>
        Assert.That(Traits.TranslateFailure(new InvalidOperationException(), KeyFunded, "Test", NullLogger.Instance), Is.Null);

    private static Task<AiHttpException?> FailureFor(HttpStatusCode status, string body) =>
        FailureFor(_ => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static async Task<AiHttpException?> FailureFor(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        using var clients = new UserKeyChatClients(maxRetries: 0, anthropicHandler: new RecordingHandler(respond));

        Exception? thrown = null;
        try
        {
            await clients.For(Key, Model, () => Secret).GetResponseAsync("Who sank the Gull?");
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.That(thrown, Is.Not.Null, "the call should have failed");
        return Traits.TranslateFailure(thrown!, KeyFunded, "Test", NullLogger.Instance);
    }

    private static HttpResponseMessage Message(string text) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent($$$"""
                {"id":"msg_1","type":"message","role":"assistant","model":"claude-sonnet-5-5",
                 "content":[{"type":"text","text":{{{System.Text.Json.JsonSerializer.Serialize(text)}}}}],
                 "stop_reason":"end_turn","stop_sequence":null,
                 "usage":{"input_tokens":10,"output_tokens":1,"cache_read_input_tokens":0,"cache_creation_input_tokens":0}}
                """, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage Stream(string events) =>
        new(HttpStatusCode.OK) { Content = new StringContent(events.Replace("\r\n", "\n"), Encoding.UTF8, "text/event-stream") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<JsonNode> Bodies { get; } = [];

        /// <summary>The bearer key on each request, read as it is sent: the SDK disposes its
        /// requests afterwards. The SDK sends an API key as <c>Authorization: Bearer</c>.</summary>
        public List<string?> ApiKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            ApiKeys.Add(request.Headers.Authorization?.Parameter);
            if (request.Content is not null)
            {
                Bodies.Add(JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!);
            }

            return respond(request);
        }
    }
}
