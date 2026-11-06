using System.ClientModel;
using System.ClientModel.Primitives;
using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using OpenAI;
using OpenAI.Images;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Chat clients, and image clients for cover art (feature 38), built with users' own keys
/// (feature 39), cached so a busy world doesn't decrypt
/// its creator's key and build a client on every call. The cache key includes the key's
/// <see cref="UserAiKey.UpdatedAt"/>, so a replaced key is never served from the cache; entries
/// slide out after ten minutes idle and the cache holds at most 500.
/// </summary>
public sealed class UserKeyChatClients : IDisposable
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Anthropic requires <c>max_tokens</c> on every call, where OpenAI defaults to the model's
    /// own ceiling; this stands in for calls that name none. Thinking counts against it, and the
    /// executor's timeout, not this, is what stops a long answer.
    /// </summary>
    public const int AnthropicDefaultMaxTokens = 32_000;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 500 });
    private readonly int _maxRetries;
    private readonly HttpClient _compatibleHttp;
    private readonly HttpClient _anthropicHttp;
    private readonly HttpClient _geminiHttp;
    private readonly HttpClient? _openAiHttp;

    /// <param name="maxRetries">The host's SDK retry count, as for Nornis's own clients
    /// (<see cref="AzureOpenAiClientFactory"/>): the Worker's queue owns its retries, the API allows one.</param>
    /// <param name="compatibleHandler">What OpenAI-compatible endpoints are reached through; by
    /// default <see cref="PublicEndpointGuard"/>'s handler, since their URLs are users' own. Tests
    /// pass a fake.</param>
    /// <param name="anthropicHandler">What Anthropic is reached through; tests pass a fake.</param>
    /// <param name="geminiHandler">What Gemini is reached through; tests pass a fake.</param>
    /// <param name="openAiHandler">What OpenAI is reached through; by default the SDK's own
    /// transport, as before. Tests pass a fake.</param>
    public UserKeyChatClients(
        int maxRetries,
        HttpMessageHandler? compatibleHandler = null,
        HttpMessageHandler? anthropicHandler = null,
        HttpMessageHandler? geminiHandler = null,
        HttpMessageHandler? openAiHandler = null)
    {
        _maxRetries = maxRetries;

        // One client for every compatible endpoint, so connections pool; the executor owns the
        // call's timeout, not HttpClient.
        _compatibleHttp = new HttpClient(compatibleHandler ?? PublicEndpointGuard.CreateHandler())
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };

        // Shared for pooling, and without HttpClient's 100-second default, which would cut a long
        // Prep draft off mid-stream.
        _anthropicHttp = anthropicHandler is null ? new HttpClient() : new HttpClient(anthropicHandler);
        _anthropicHttp.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        _geminiHttp = geminiHandler is null ? new HttpClient() : new HttpClient(geminiHandler);
        _geminiHttp.Timeout = System.Threading.Timeout.InfiniteTimeSpan;

        _openAiHttp = openAiHandler is null ? null : new HttpClient(openAiHandler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    /// <summary>The client for <paramref name="key"/> on <paramref name="model"/>, speaking
    /// <paramref name="shape"/>. <paramref name="secret"/> is asked for only when the client has
    /// to be built.</summary>
    /// <exception cref="NotSupportedException"><see cref="AiClientShape.Responses"/> for a key
    /// that isn't OpenAI's.</exception>
    public IChatClient For(UserAiKey key, string model, Func<string> secret, AiClientShape shape = AiClientShape.Chat)
    {
        if (shape == AiClientShape.Responses && key.Provider != AiProvider.OpenAi)
        {
            throw new NotSupportedException($"Only an OpenAI key is given a Responses client, not {key.Provider}.");
        }

        return _cache.GetOrCreate((key.Id, key.UpdatedAt, model, shape), entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = Idle;
            return shape == AiClientShape.Responses ? BuildResponses(model, secret()) : Build(key, model, secret());
        })!;
    }

    /// <summary>
    /// The image client for <paramref name="key"/> on <paramref name="model"/> (feature 38), cached
    /// the same way. Only an OpenAI key makes images today; an OpenAI-compatible endpoint is a chat
    /// endpoint, and Gemini joins with its client.
    /// </summary>
    public ImageClient ImagesFor(UserAiKey key, string model, Func<string> secret) =>
        _cache.GetOrCreate((key.Id, key.UpdatedAt, model, "images"), entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = Idle;
            return key.Provider == AiProvider.OpenAi
                ? new OpenAIClient(new ApiKeyCredential(secret()), new OpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(_maxRetries) })
                    .GetImageClient(model)
                : throw new NotSupportedException($"No image client is built for {key.Provider} keys.");
        })!;

    private IChatClient Build(UserAiKey key, string model, string secret)
    {
        if (key.Provider == AiProvider.Anthropic)
        {
            var anthropic = new AnthropicClient
            {
                ApiKey = secret,
                HttpClient = _anthropicHttp,
                MaxRetries = _maxRetries,
                Timeout = TimeSpan.FromHours(1),
            };
            return new AnthropicPromptCaching(anthropic.AsIChatClient(model, AnthropicDefaultMaxTokens));
        }

        if (key.Provider == AiProvider.Gemini)
        {
            // The Gemini Developer API, never Vertex: said outright, because the SDK otherwise
            // reads GOOGLE_GENAI_USE_VERTEXAI and friends from the environment. Gemini caches a
            // repeated prefix by itself (implicit caching), so nothing marks a breakpoint.
            var gemini = new Google.GenAI.Client(
                enterprise: false,
                apiKey: secret,
                httpOptions: new Google.GenAI.Types.HttpOptions
                {
                    RetryOptions = _maxRetries > 0 ? new Google.GenAI.Types.HttpRetryOptions { Attempts = _maxRetries + 1 } : null,
                },
                clientOptions: new Google.GenAI.Types.ClientOptions { HttpClientFactory = () => _geminiHttp });
            return new GeminiBilledUsage(gemini.AsIChatClient(model));
        }

        var options = OpenAiOptions();
        switch (key.Provider)
        {
            case AiProvider.OpenAi:
                break;

            case AiProvider.OpenAiCompatible when Uri.TryCreate(key.BaseUrl, UriKind.Absolute, out var endpoint):
                options.Endpoint = endpoint;
                options.Transport = new HttpClientPipelineTransport(_compatibleHttp);
                break;

            case AiProvider.OpenAiCompatible:
                throw new InvalidOperationException($"OpenAI-compatible key {key.Id} has no usable base URL.");

            default:
                throw new NotSupportedException($"No client is built for {key.Provider} keys yet (feature 39 Phase C).");
        }

        // The same stream-reading wrapper as Nornis's own clients: every OpenAI-protocol stream
        // goes through the SDK's reader rather than the adapter's.
        return new AzureOpenAiChatClient(new OpenAIClient(new ApiKeyCredential(secret), options).GetChatClient(model));
    }

    /// <summary>
    /// OpenAI's Responses API, for a call that searches (feature 40). The MEAI adapter reads its
    /// stream: <see cref="AzureOpenAiChatClient"/>'s reader is for Chat Completions streams. The
    /// SDK marks the client as for evaluation (OPENAI001); it is the only API OpenAI offers search
    /// on, so the warning is accepted here and nowhere else.
    /// </summary>
    private IChatClient BuildResponses(string model, string secret)
    {
#pragma warning disable OPENAI001
        return new OpenAIClient(new ApiKeyCredential(secret), OpenAiOptions()).GetResponsesClient().AsIChatClient(model);
#pragma warning restore OPENAI001
    }

    private OpenAIClientOptions OpenAiOptions()
    {
        var options = new OpenAIClientOptions { RetryPolicy = new ClientRetryPolicy(_maxRetries) };
        if (_openAiHttp is not null)
        {
            options.Transport = new HttpClientPipelineTransport(_openAiHttp);
        }

        return options;
    }

    public void Dispose()
    {
        _cache.Dispose();
        _compatibleHttp.Dispose();
        _anthropicHttp.Dispose();
        _geminiHttp.Dispose();
        _openAiHttp?.Dispose();
    }
}
