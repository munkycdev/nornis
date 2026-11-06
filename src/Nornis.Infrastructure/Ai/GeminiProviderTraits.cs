using System.Net;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Google Gemini through Google's official SDK (Google.GenAI) and its own <see cref="IChatClient"/>
/// adapter (feature 39 Phase C3). Only users' own keys reach it. The adapter sends
/// <see cref="ChatOptions.ResponseFormat"/> as <c>responseJsonSchema</c> and
/// <see cref="ChatOptions.MaxOutputTokens"/> as <c>maxOutputTokens</c>; with no ceiling the
/// model's own applies, as on OpenAI.
/// </summary>
public sealed class GeminiProviderTraits : IAiProviderTraits
{
    public static readonly GeminiProviderTraits Instance = new();

    private GeminiProviderTraits()
    {
    }

    /// <summary>Web search (feature 40) is Google Search grounding, which the adapter sends as
    /// <c>tools: [{ googleSearch: {} }]</c>. Gemini has no per-call cap; the prompt asks for one.</summary>
    public ChatOptions? ChatOptions(AiCallOptions? options) =>
        AnthropicProviderTraits.AdapterChatOptions(options, searchTool: _ => new HostedWebSearchTool());

    /// <summary>
    /// The adapter reports grounding as a <see cref="WebSearchToolCallContent"/> whose raw form is
    /// Gemini's <see cref="GroundingMetadata"/>: the queries it ran (Gemini 3 bills each one), the
    /// pages it grounded the answer in, and the search suggestions its terms ask to be shown.
    /// Read whole each time, because a later chunk restates the call's grounding rather than adding to it.
    /// </summary>
    public void ReadWebResearch(ChatResponseUpdate update, WebResearchCollector into)
    {
        WebSearchReading.AddCitedPages(update, into);
        foreach (var content in update.Contents)
        {
            if (content is WebSearchToolCallContent { RawRepresentation: GroundingMetadata grounding })
            {
                into.SetSearches(grounding.WebSearchQueries?.Count ?? 0);
                foreach (var chunk in grounding.GroundingChunks ?? [])
                {
                    into.AddPage(chunk.Web?.Title, chunk.Web?.Uri);
                }

                if (grounding.SearchEntryPoint?.RenderedContent is { } suggestions)
                {
                    into.SuggestionsHtml = suggestions;
                }
            }
        }
    }

    /// <summary>A 400 that names search grounding: the model doesn't take it.</summary>
    public bool RefusedWebSearch(Exception ex) =>
        ex is ApiException { StatusCode: (int)HttpStatusCode.BadRequest } api
        && (api.Message.Contains("google_search", StringComparison.OrdinalIgnoreCase)
            || api.Message.Contains("googleSearch", StringComparison.OrdinalIgnoreCase)
            || api.Message.Contains("grounding", StringComparison.OrdinalIgnoreCase));

    public AiHttpException? TranslateFailure(Exception ex, AiFunding funding, string operation, ILogger logger)
    {
        switch (ex)
        {
            // Both an error status and an error inside the stream arrive as ApiException, with
            // the status Google put in the error's "code".
            case ApiException api when api.StatusCode > 0:
                return FromStatus(api, api.StatusCode, funding, operation, logger);

            case ApiException api:
                logger.LogError(api, "{Operation} AI call failed with an unreadable Gemini error.", operation);
                return new AiHttpException($"{operation} AI call failed: the provider reported an error", 502, api);

            // Anything else the HTTP stack throws: the provider wasn't reached.
            case HttpRequestException http:
                logger.LogError(http, "{Operation} AI call couldn't reach Gemini.", operation);
                return new AiHttpException($"{operation} AI call couldn't reach the provider.", null, http);

            default:
                return null;
        }
    }

    private static AiHttpException FromStatus(ApiException ex, int status, AiFunding funding, string operation, ILogger logger)
    {
        if (funding.FundedBy == AiFundedBy.Key)
        {
            // Gemini answers a bad or expired key with a 400 (INVALID_ARGUMENT, reason
            // API_KEY_INVALID), not a 401; a 403 is a key without access to the API.
            if (status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden || IsBadKey(status, ex.Message))
            {
                logger.LogWarning("{Operation} AI call was refused: the key holder's {Provider} key was rejected (HTTP {Status}).",
                    operation, funding.Provider, status);
                return new AiKeyRejectedException(funding.Provider, ex);
            }

            // A 429 is Google's RESOURCE_EXHAUSTED for a rate limit and a daily quota alike, so it
            // stays retryable; only an explicit payment refusal reads as out of credit.
            if (status == (int)HttpStatusCode.PaymentRequired)
            {
                logger.LogWarning("{Operation} AI call was refused: the key holder's {Provider} account is out of credit.",
                    operation, funding.Provider);
                return new AiCreditExhaustedException(funding.Provider, ex);
            }
        }

        // Google's error messages name the reason and never echo the key.
        logger.LogError(ex, "{Operation} AI call failed with status {Status}: {Message}", operation, status, ex.Message);
        return new AiHttpException($"{operation} AI call failed: HTTP {status}", status, ex);
    }

    internal static bool IsBadKey(int status, string message) =>
        status == (int)HttpStatusCode.BadRequest
        && (message.Contains("API_KEY_INVALID", StringComparison.Ordinal)
            || message.Contains("API key not valid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("API key expired", StringComparison.OrdinalIgnoreCase));
}
