using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Asks a key's provider which models it can run (feature 39), through the same request as
/// <see cref="AiKeyChecker"/>, and keeps the answer an hour per key version. A failure is not
/// kept, so the next visit asks again.
/// </summary>
public sealed class AiModelLister : IAiModelLister
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    /// <summary>OpenAI's list holds every model the account can reach. These name ones that
    /// don't answer Chat Completions (audio, images, embeddings, the Responses-only models).</summary>
    private static readonly string[] OpenAiNonChat =
    [
        "audio", "realtime", "tts", "transcribe", "image", "dall-e", "embedding", "moderation",
        "search", "instruct", "codex", "-pro", "deep-research", "computer-use", "sora", "whisper",
    ];

    /// <summary>Google's list also holds the Live, speech, image and embedding models, some of
    /// which answer generateContent but not as a chat model.</summary>
    private static readonly string[] GeminiNonChat =
    [
        "live", "tts", "image", "embedding", "native-audio", "transcribe", "translate", "omni", "robotics", "computer-use",
    ];

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiModelLister> _logger;

    public AiModelLister(HttpClient http, IMemoryCache cache, ILogger<AiModelLister> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AiListedModel>?> ListChatModelsAsync(UserAiKey key, string secret, CancellationToken ct)
    {
        if (key.Provider is not (AiProvider.OpenAi or AiProvider.Anthropic or AiProvider.Gemini))
        {
            return null;
        }

        var cacheKey = (nameof(AiModelLister), key.Id, key.UpdatedAt);
        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<AiListedModel>? cached))
        {
            return cached;
        }

        var listed = await AskAsync(key.Provider, secret, ct);
        if (listed is not null)
        {
            _cache.Set(cacheKey, listed, Lifetime);
        }

        return listed;
    }

    private async Task<IReadOnlyList<AiListedModel>?> AskAsync(AiProvider provider, string secret, CancellationToken ct)
    {
        // Anthropic and Google page their lists; one page of a thousand is all of it.
        var query = provider switch
        {
            AiProvider.Anthropic => "?limit=1000",
            AiProvider.Gemini => "?pageSize=1000",
            _ => null,
        };
        using var request = AiKeyChecker.ModelsRequest(provider, secret, null, query);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(AiKeyChecker.Timeout);
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Listing {Provider} models answered HTTP {Status}.", provider, (int)response.StatusCode);
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: timeout.Token);
            return Parse(provider, document.RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogInformation("Listing {Provider} models failed: {Why}", provider, ex.Message);
            return null;
        }
    }

    /// <summary>The chat models in a provider's answer, sorted by id: <c>{"data":[{"id":…}]}</c>
    /// from OpenAI and Anthropic, <c>{"models":[{"name":"models/…"}]}</c> from Google.</summary>
    internal static IReadOnlyList<AiListedModel> Parse(AiProvider provider, JsonElement root)
    {
        if (provider == AiProvider.Gemini)
        {
            return ParseGemini(root);
        }

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<AiListedModel>();
        foreach (var entry in data.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("id", out var idElement) || idElement.GetString() is not { Length: > 0 and <= 80 } id
                || !IsChatModel(provider, id))
            {
                continue;
            }

            var name = entry.TryGetProperty("display_name", out var display) && display.GetString() is { Length: > 0 } shown
                ? shown
                : id;
            models.Add(new AiListedModel(id, name));
        }

        return models.DistinctBy(m => m.Id).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Google lists every model with the methods it answers; a chat model answers
    /// <c>generateContent</c>. Its name carries a <c>models/</c> prefix the SDK adds itself.</summary>
    private static IReadOnlyList<AiListedModel> ParseGemini(JsonElement root)
    {
        if (!root.TryGetProperty("models", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<AiListedModel>();
        foreach (var entry in list.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("name", out var nameElement) || nameElement.GetString() is not { } name
                || !entry.TryGetProperty("supportedGenerationMethods", out var methods) || methods.ValueKind != JsonValueKind.Array
                || !methods.EnumerateArray().Any(m => m.GetString() == "generateContent"))
            {
                continue;
            }

            var id = name.StartsWith("models/", StringComparison.Ordinal) ? name["models/".Length..] : name;
            if (id.Length is 0 or > 80 || !IsChatModel(AiProvider.Gemini, id))
            {
                continue;
            }

            var shown = entry.TryGetProperty("displayName", out var display) && display.GetString() is { Length: > 0 } text
                ? text
                : id;
            models.Add(new AiListedModel(id, shown));
        }

        return models.DistinctBy(m => m.Id).OrderBy(m => m.Id, StringComparer.Ordinal).ToList();
    }

    internal static bool IsChatModel(AiProvider provider, string id) => provider switch
    {
        AiProvider.Gemini => id.StartsWith("gemini-", StringComparison.Ordinal)
            && !GeminiNonChat.Any(part => id.Contains(part, StringComparison.Ordinal)),
        AiProvider.Anthropic => id.StartsWith("claude-", StringComparison.Ordinal),
        AiProvider.OpenAi => (id.StartsWith("gpt-", StringComparison.Ordinal)
                || id.StartsWith("chatgpt-", StringComparison.Ordinal)
                || (id.Length > 1 && id[0] == 'o' && char.IsAsciiDigit(id[1])))
            && !OpenAiNonChat.Any(part => id.Contains(part, StringComparison.Ordinal)),
        _ => false,
    };
}
