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
/// Gemini keys (feature 39 Phase C3), through Google's SDK and its <see cref="IChatClient"/>
/// adapter against a fake Gemini: what reaches the wire, what each failure means, and that
/// thinking is billed as output.
/// </summary>
[TestFixture]
public class GeminiProviderTraitsTests
{
    private const string Secret = "AIza-redaction-canary-1234";
    private const string Model = "gemini-3.8-flash";

    private static readonly GeminiProviderTraits Traits = GeminiProviderTraits.Instance;

    private static readonly AiFunding KeyFunded = new(AiProvider.Gemini, Model, AiFundedBy.Key, AiKeyId: Guid.NewGuid());

    private static readonly UserAiKey Key = new()
    {
        Id = Guid.NewGuid(),
        Provider = AiProvider.Gemini,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private const string Answer = """
        {"candidates":[{"content":{"role":"model","parts":[{"text":"Voss"}]},"finishReason":"STOP","index":0}],
         "usageMetadata":{"promptTokenCount":10,"cachedContentTokenCount":2,"candidatesTokenCount":4,"thoughtsTokenCount":20,"totalTokenCount":34},
         "modelVersion":"gemini-3.8-flash","responseId":"r1"}
        """;

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
    public async Task A_schema_and_a_ceiling_reach_the_wire_as_the_generation_config()
    {
        const string schema = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"],"additionalProperties":false}""";
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, Answer));
        using var clients = new UserKeyChatClients(maxRetries: 0, geminiHandler: handler);

        var response = await clients.For(Key, Model, () => Secret).GetResponseAsync(
            [new ChatMessage(ChatRole.System, "You are the Loremaster."), new ChatMessage(ChatRole.User, "Who sank the Gull?")],
            Traits.ChatOptions(new AiCallOptions(new AiJsonSchema("who", schema), MaxOutputTokens: 2_500)));

        var body = handler.Bodies.Single();
        var config = body["generationConfig"];
        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.EqualTo("Voss"));
            Assert.That(handler.Uris.Single().AbsolutePath, Does.EndWith("/models/gemini-3.8-flash:generateContent"));
            Assert.That(handler.Uris.Single().Host, Is.EqualTo("generativelanguage.googleapis.com"), "the Developer API, never Vertex");
            Assert.That(handler.Uris.Single().Query, Does.Not.Contain(Secret), "the key never travels in the URL");
            Assert.That(handler.ApiKeys.Single(), Is.EqualTo(Secret));
            Assert.That(config?["maxOutputTokens"]?.GetValue<int>(), Is.EqualTo(2_500), body.ToJsonString());
            Assert.That(config?["responseMimeType"]?.GetValue<string>(), Is.EqualTo("application/json"), body.ToJsonString());
            Assert.That(JsonNode.DeepEquals(config?["responseJsonSchema"], JsonNode.Parse(schema)), Is.True, body.ToJsonString());
            Assert.That(body["systemInstruction"]?.ToJsonString(), Does.Contain("You are the Loremaster."), body.ToJsonString());
        });
    }

    [Test]
    public async Task Thinking_is_billed_as_output_and_cached_input_is_reported()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK, Answer));
        using var clients = new UserKeyChatClients(maxRetries: 0, geminiHandler: handler);

        var response = await clients.For(Key, Model, () => Secret).GetResponseAsync("Who sank the Gull?");

        Assert.Multiple(() =>
        {
            Assert.That(response.Usage?.OutputTokenCount, Is.EqualTo(24), "4 answer tokens and 20 of thinking, both billed as output");
            Assert.That(response.Usage?.ReasoningTokenCount, Is.EqualTo(20));
            Assert.That(response.Usage?.InputTokenCount, Is.EqualTo(10));
            Assert.That(response.Usage?.CachedInputTokenCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Thinking_is_billed_as_output_when_streamed_too()
    {
        var handler = new RecordingHandler(_ => Stream($"data: {Answer.ReplaceLineEndings(" ")}\n\n"));
        using var clients = new UserKeyChatClients(maxRetries: 0, geminiHandler: handler);

        UsageDetails? usage = null;
        var text = new StringBuilder();
        await foreach (var update in clients.For(Key, Model, () => Secret).GetStreamingResponseAsync("Who sank the Gull?"))
        {
            text.Append(update.Text);
            usage = update.Contents.OfType<UsageContent>().LastOrDefault()?.Details ?? usage;
        }

        Assert.Multiple(() =>
        {
            Assert.That(text.ToString(), Is.EqualTo("Voss"));
            Assert.That(usage?.OutputTokenCount, Is.EqualTo(24));
            Assert.That(handler.Uris.Single().AbsolutePath, Does.EndWith(":streamGenerateContent"));
        });
    }

    [Test]
    public void Folding_leaves_a_call_without_thinking_alone()
    {
        var usage = new UsageDetails { OutputTokenCount = 4 };

        GeminiBilledUsage.Bill(usage);

        Assert.That(usage.OutputTokenCount, Is.EqualTo(4));
    }

    [TestCase(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key.", "API_KEY_INVALID")]
    [TestCase(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "API key expired. Please renew the API key.", "API_KEY_INVALID")]
    [TestCase(HttpStatusCode.Forbidden, "PERMISSION_DENIED", "Method doesn't allow unregistered callers.", "")]
    [TestCase(HttpStatusCode.Unauthorized, "UNAUTHENTICATED", "Request had invalid authentication credentials.", "")]
    public async Task A_refused_key_is_rejected(HttpStatusCode status, string code, string message, string reason)
    {
        var translated = await FailureFor(status, Error((int)status, code, message, reason));

        Assert.That(translated, Is.InstanceOf<AiKeyRejectedException>());
    }

    [Test]
    public async Task A_payment_refusal_is_out_of_credit()
    {
        var translated = await FailureFor(HttpStatusCode.PaymentRequired, Error(402, "FAILED_PRECONDITION", "Billing required.", ""));

        Assert.That(translated, Is.InstanceOf<AiCreditExhaustedException>());
    }

    [TestCase(HttpStatusCode.BadRequest, "INVALID_ARGUMENT", "Invalid JSON payload received.")]
    [TestCase(HttpStatusCode.TooManyRequests, "RESOURCE_EXHAUSTED", "You exceeded your current quota, please check your plan and billing details.")]
    [TestCase(HttpStatusCode.InternalServerError, "INTERNAL", "An internal error has occurred.")]
    [TestCase(HttpStatusCode.ServiceUnavailable, "UNAVAILABLE", "The model is overloaded. Please try again later.")]
    public async Task Any_other_status_is_kept_so_the_retry_rules_see_it(HttpStatusCode status, string code, string message)
    {
        var translated = await FailureFor(status, Error((int)status, code, message, ""));

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
    public async Task An_error_inside_the_stream_keeps_its_status()
    {
        var handler = new RecordingHandler(_ => Stream(
            $"data: {Error(503, "UNAVAILABLE", "The model is overloaded.", "").ReplaceLineEndings(" ")}\n\n"));
        using var clients = new UserKeyChatClients(maxRetries: 0, geminiHandler: handler);

        Exception? thrown = null;
        try
        {
            await foreach (var _ in clients.For(Key, Model, () => Secret).GetStreamingResponseAsync("Who sank the Gull?"))
            {
            }
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.That(thrown, Is.Not.Null, "the stream should have failed");
        Assert.That(Traits.TranslateFailure(thrown!, KeyFunded, "Test", NullLogger.Instance)?.StatusCode, Is.EqualTo(503), thrown!.ToString());
    }

    [Test]
    public void Something_else_is_not_this_providers_to_name() =>
        Assert.That(Traits.TranslateFailure(new InvalidOperationException(), KeyFunded, "Test", NullLogger.Instance), Is.Null);

    private static string Error(int code, string status, string message, string reason)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message, ["status"] = status };
        if (reason.Length > 0)
        {
            error["details"] = new JsonArray(new JsonObject
            {
                ["@type"] = "type.googleapis.com/google.rpc.ErrorInfo",
                ["reason"] = reason,
                ["domain"] = "googleapis.com",
            });
        }

        return new JsonObject { ["error"] = error }.ToJsonString();
    }

    private static Task<AiHttpException?> FailureFor(HttpStatusCode status, string body) =>
        FailureFor(_ => Json(status, body));

    private static async Task<AiHttpException?> FailureFor(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        using var clients = new UserKeyChatClients(maxRetries: 0, geminiHandler: new RecordingHandler(respond));

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

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Stream(string events) =>
        new(HttpStatusCode.OK) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Uris { get; } = [];

        public List<JsonNode> Bodies { get; } = [];

        /// <summary>Read as each request is sent: the SDK disposes its requests afterwards.</summary>
        public List<string?> ApiKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Uris.Add(request.RequestUri!);
            ApiKeys.Add(request.Headers.TryGetValues("x-goog-api-key", out var keys) ? keys.Single() : null);
            if (request.Content is not null)
            {
                Bodies.Add(JsonNode.Parse(await request.Content.ReadAsStringAsync(ct))!);
            }

            return respond(request);
        }
    }
}
