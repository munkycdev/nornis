using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// What an output ceiling looks like on the wire. On 2026-07-27 a ceiling took every AI feature
/// down: Azure.AI.OpenAI 2.1.0 sent it as <c>max_tokens</c>, which the gpt-5.4 deployments refuse
/// with HTTP 400. The clients now use the plain OpenAI SDK, and these tests read the request body
/// it actually sends, through the same <see cref="AzureOpenAiChatClient"/> production uses.
///
/// They prove the name on the wire, not that the deployment accepts it. That is
/// <see cref="LiveOutputCeilingTests"/>, run by hand. Together they are what lets Prep's drafting
/// client carry a ceiling; if an SDK bump brings <c>max_tokens</c> back, these go red first.
/// </summary>
[TestFixture]
public class OutputCeilingWireTests
{
    [Test]
    public async Task Streaming_ACeilingOnTheSdkOptions_IsSentAsMaxCompletionTokens()
    {
        var capture = new CapturingHandler(StreamResponse);
        var client = new AzureOpenAiChatClient(SdkClient(capture));
        var options = new ChatOptions
        {
            RawRepresentationFactory = _ => new ChatCompletionOptions { MaxOutputTokenCount = 2500 },
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "Describe a harbor town.")], options))
        {
        }

        AssertCeilingOnTheWire(capture.Body, 2500);
    }

    [Test]
    public async Task NonStreaming_ACeilingOnChatOptions_IsSentAsMaxCompletionTokens()
    {
        var capture = new CapturingHandler(CompletionResponse);
        var client = new AzureOpenAiChatClient(SdkClient(capture));

        await client.GetResponseAsync([new(ChatRole.User, "Describe a harbor town.")],
            new ChatOptions { MaxOutputTokens = 2500 });

        AssertCeilingOnTheWire(capture.Body, 2500);
    }

    private static void AssertCeilingOnTheWire(string? body, int expected)
    {
        Assert.That(body, Is.Not.Null, "no request reached the transport");
        var json = JsonNode.Parse(body!)!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(json.ContainsKey("max_tokens"), Is.False,
                "max_tokens is the name the gpt-5.4 deployments reject with HTTP 400");
            Assert.That(json["max_completion_tokens"]?.GetValue<int>(), Is.EqualTo(expected));
        });
    }

    private static ChatClient SdkClient(HttpMessageHandler handler) =>
        new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://example.openai.azure.com/openai/v1/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            RetryPolicy = new ClientRetryPolicy(0),
        }).GetChatClient("nornis-ask");

    private static HttpResponseMessage StreamResponse()
    {
        const string stream = """
            data: {"choices":[{"delta":{"content":"Salt","role":"assistant"},"finish_reason":null,"index":0}],"created":1790624127,"id":"c1","model":"gpt-5.4","object":"chat.completion.chunk"}

            data: {"choices":[{"delta":{},"finish_reason":"stop","index":0}],"created":1790624127,"id":"c1","model":"gpt-5.4","object":"chat.completion.chunk"}

            data: [DONE]


            """;
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(stream.ReplaceLineEndings("\n"), Encoding.UTF8),
        };
        response.Content.Headers.ContentType = new("text/event-stream");
        return response;
    }

    private static HttpResponseMessage CompletionResponse() =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"id":"c1","object":"chat.completion","created":1790624127,"model":"gpt-5.4",
                 "choices":[{"index":0,"message":{"role":"assistant","content":"Salt"},"finish_reason":"stop"}],
                 "usage":{"prompt_tokens":5,"completion_tokens":1,"total_tokens":6}}
                """, Encoding.UTF8, "application/json"),
        };

    private sealed class CapturingHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return respond();
        }
    }
}
