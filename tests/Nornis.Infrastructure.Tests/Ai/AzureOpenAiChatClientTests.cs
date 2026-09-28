using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Microsoft.Extensions.AI;
using Nornis.Infrastructure.Ai;
using NUnit.Framework;
using OpenAI;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Tests.Ai;

/// <summary>
/// Streaming against Azure's real chunk shapes. The stream below was captured from the nornis-ask
/// deployment on 2026-09-28 and trimmed to its distinct shapes: the prompt-filter chunk with no
/// choices, the opening role chunk, text, the content-filter chunk with no <c>delta</c> that Azure
/// appends to every stream, and the usage-only chunk. It goes through the real OpenAI SDK over a
/// stubbed HTTP transport, so what is under test is exactly what runs in production.
/// </summary>
[TestFixture]
public class AzureOpenAiChatClientTests
{
    private const string AzureStream = """
        data: {"choices":[],"created":0,"id":"","model":"","object":"","prompt_filter_results":[{"prompt_index":0,"content_filter_results":{"hate":{"filtered":false,"severity":"safe"},"jailbreak":{"detected":false,"filtered":false},"self_harm":{"filtered":false,"severity":"safe"},"sexual":{"filtered":false,"severity":"safe"},"violence":{"filtered":false,"severity":"safe"}}}]}

        data: {"choices":[{"content_filter_results":{},"delta":{"content":"","refusal":null,"role":"assistant"},"finish_reason":null,"index":0,"logprobs":null}],"created":1790624127,"id":"chatcmpl-ETBQFPWpiGbvFHwUq40ohaHVcUezC","model":"gpt-5.4-2026-03-05","obfuscation":"BlVrFXYw","object":"chat.completion.chunk","service_tier":"default","system_fingerprint":null,"usage":null}

        data: {"choices":[{"content_filter_results":{},"delta":{"content":"The"},"finish_reason":null,"index":0,"logprobs":null}],"created":1790624127,"id":"chatcmpl-ETBQFPWpiGbvFHwUq40ohaHVcUezC","model":"gpt-5.4-2026-03-05","obfuscation":"fYGG0g3","object":"chat.completion.chunk","service_tier":"default","system_fingerprint":null,"usage":null}

        data: {"choices":[{"content_filter_results":{},"delta":{"content":" harbor"},"finish_reason":null,"index":0,"logprobs":null}],"created":1790624127,"id":"chatcmpl-ETBQFPWpiGbvFHwUq40ohaHVcUezC","model":"gpt-5.4-2026-03-05","obfuscation":"J6d","object":"chat.completion.chunk","service_tier":"default","system_fingerprint":null,"usage":null}

        data: {"choices":[{"content_filter_offsets":{"check_offset":504,"start_offset":71,"end_offset":504},"content_filter_results":{"hate":{"filtered":false,"severity":"safe"},"protected_material_code":{"detected":false,"filtered":false},"protected_material_text":{"detected":false,"filtered":false},"self_harm":{"filtered":false,"severity":"safe"},"sexual":{"filtered":false,"severity":"safe"},"violence":{"filtered":false,"severity":"safe"}},"finish_reason":null,"index":0}],"created":0,"id":"","model":"","object":""}

        data: {"choices":[],"created":1790624127,"id":"chatcmpl-ETBQFPWpiGbvFHwUq40ohaHVcUezC","latency_checkpoint":{"engine_tbt_ms":31,"engine_ttft_ms":85,"engine_ttlt_ms":3001,"pre_inference_ms":67,"service_tbt_ms":31,"service_ttft_ms":531,"service_ttlt_ms":3444,"user_visible_ttft_ms":464},"model":"gpt-5.4-2026-03-05","obfuscation":"YixFR9HL9mLAio","object":"chat.completion.chunk","routing":{"serving_pipereplica":"d20260920002635-601dfeb8-default-r2-dp0-default"},"service_tier":"default","system_fingerprint":null,"usage":{"completion_tokens":93,"completion_tokens_details":{"accepted_prediction_tokens":0,"audio_tokens":0,"reasoning_tokens":0,"rejected_prediction_tokens":0},"prompt_tokens":14,"prompt_tokens_details":{"audio_tokens":0,"cached_tokens":0},"total_tokens":107}}

        data: [DONE]


        """;

    [Test]
    public async Task Streaming_ReadsAzuresWholeStream_TextThenUsage()
    {
        var client = new AzureOpenAiChatClient(SdkClient());

        var text = new StringBuilder();
        UsageDetails? usage = null;
        await foreach (var update in client.GetStreamingResponseAsync([new(ChatRole.User, "Describe a harbor town.")]))
        {
            text.Append(update.Text);
            usage = update.Contents.OfType<UsageContent>().SingleOrDefault()?.Details ?? usage;
        }

        Assert.That(text.ToString(), Is.Not.Empty.And.StartWith("The"));
        Assert.That(usage, Is.Not.Null, "the usage-only chunk after the content-filter chunk still arrives");
        Assert.That(usage!.TotalTokenCount, Is.GreaterThan(0));
    }

    [Test]
    public void Streaming_ThroughTheStockAdapter_StillThrowsOnAzuresContentFilterChunk()
    {
        // Why AzureOpenAiChatClient overrides streaming at all. When this starts failing, the
        // adapter handles the chunk and the override can be deleted in favour of AsIChatClient().
        var adapter = SdkClient().AsIChatClient();

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in adapter.GetStreamingResponseAsync([new(ChatRole.User, "Describe a harbor town.")]))
            {
            }
        });
    }

    [Test]
    public void Streaming_OptionsOtherThanTheRawFactory_AreRefusedNotDropped()
    {
        var client = new AzureOpenAiChatClient(SdkClient());

        Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in client.GetStreamingResponseAsync(
                [new(ChatRole.User, "Q")], new ChatOptions { MaxOutputTokens = 10 }))
            {
            }
        });
    }

    private static ChatClient SdkClient() =>
        new OpenAIClient(new ApiKeyCredential("test"), new OpenAIClientOptions
        {
            Endpoint = new Uri("https://example.openai.azure.com/openai/v1/"),
            Transport = new HttpClientPipelineTransport(new HttpClient(new ReplayHandler())),
            RetryPolicy = new ClientRetryPolicy(0),
        }).GetChatClient("nornis-ask");

    private sealed class ReplayHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(AzureStream.ReplaceLineEndings("\n"), Encoding.UTF8),
            };
            response.Content.Headers.ContentType = new("text/event-stream");
            return Task.FromResult(response);
        }
    }
}
