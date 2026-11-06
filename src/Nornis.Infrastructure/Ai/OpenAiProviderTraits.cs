using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// The OpenAI wire protocol: OpenAI itself, OpenAI-compatible endpoints, and Azure OpenAI's v1
/// endpoint, which Nornis's own key uses (feature 39).
/// </summary>
public sealed class OpenAiProviderTraits : IAiProviderTraits
{
    public static readonly OpenAiProviderTraits Instance = new();

    private OpenAiProviderTraits()
    {
    }

    /// <summary>
    /// Set on the SDK's own options through the factory, deliberately not through
    /// <c>ChatResponseFormat.ForJsonSchema</c> or <see cref="ChatOptions.MaxOutputTokens"/>:
    /// the adapter runs every schema given that way through its own strict-mode transform, and
    /// the output ceiling has to reach the wire as <c>max_completion_tokens</c>, which the
    /// SDK's own property does (<c>OutputCeilingWireTests</c>). A factory, not a shared
    /// instance, because the SDK mutates the options object it sends.
    ///
    /// A search call (feature 40) is bound to a Responses client (<see cref="AiClientShape.Responses"/>),
    /// so its options are the Responses API's own.
    /// </summary>
    public ChatOptions? ChatOptions(AiCallOptions? options)
    {
        if (options is null || (options.Schema is null && options.MaxOutputTokens is null && options.WebSearch is null))
        {
            return null;
        }

        if (options.WebSearch is { } search)
        {
            return SearchOptions(options, search);
        }

        return new ChatOptions
        {
            RawRepresentationFactory = _ =>
            {
                var sdkOptions = new OpenAI.Chat.ChatCompletionOptions();
                if (options.Schema is { } schema)
                {
                    sdkOptions.ResponseFormat = OpenAI.Chat.ChatResponseFormat.CreateJsonSchemaFormat(
                        jsonSchemaFormatName: schema.Name,
                        jsonSchema: BinaryData.FromString(schema.Schema),
                        jsonSchemaIsStrict: true);
                }

                if (options.MaxOutputTokens is { } ceiling)
                {
                    sdkOptions.MaxOutputTokenCount = ceiling;
                }

                return sdkOptions;
            },
        };
    }

    /// <summary>
    /// The Responses API's search tool, and nothing else, capped by <c>max_tool_calls</c>, with the
    /// ceiling as <c>max_output_tokens</c>. <c>store</c> is off: the Responses API keeps a
    /// response for 30 days unless told not to, and a Prep prompt carries Hidden truth. No schema:
    /// research is a side-conversation turn, which answers in prose.
    /// </summary>
    private static ChatOptions SearchOptions(AiCallOptions options, AiWebSearch search)
    {
        if (options.Schema is not null)
        {
            throw new NotSupportedException("A web search call answers in prose; it takes no schema.");
        }

#pragma warning disable OPENAI001 // The Responses API's options; see UserKeyChatClients.BuildResponses.
        return new ChatOptions
        {
            Tools = [new HostedWebSearchTool()],
            RawRepresentationFactory = _ => new OpenAI.Responses.CreateResponseOptions
            {
                MaxToolCallCount = search.MaxSearches,
                MaxOutputTokenCount = options.MaxOutputTokens,
                StoredOutputEnabled = false,
            },
        };
#pragma warning restore OPENAI001
    }

    /// <summary>
    /// The Responses API reports each search as a <see cref="WebSearchToolCallContent"/> (a
    /// <c>web_search_call</c> item), more than once as it runs, so searches are counted by call id;
    /// each cited page is a <see cref="CitationAnnotation"/> (<c>url_citation</c>). Every search is
    /// an item of the response, so once usage arrives at the end of the stream the count is
    /// complete, and none is a count of zero, not an unreported one.
    /// </summary>
    public void ReadWebResearch(ChatResponseUpdate update, WebResearchCollector into)
    {
        WebSearchReading.AddCitedPages(update, into);
        foreach (var content in update.Contents)
        {
            switch (content)
            {
                case WebSearchToolCallContent { CallId: { } callId }:
                    into.CountSearch(callId);
                    break;

                case UsageContent:
                    into.SetSearches(into.Searches ?? 0);
                    break;
            }
        }
    }

    /// <summary>A 400 whose body names the search tool: the model doesn't take it.</summary>
    public bool RefusedWebSearch(Exception ex) =>
        ex is ClientResultException { Status: 400 } clientResult
        && RawResponseBody(clientResult).Contains("web_search", StringComparison.OrdinalIgnoreCase);

    public AiHttpException? TranslateFailure(Exception ex, AiFunding funding, string operation, ILogger logger)
    {
        if (ex is not ClientResultException clientResult)
        {
            return null;
        }

        var body = RawResponseBody(clientResult);

        if (funding.FundedBy == AiFundedBy.Key)
        {
            // Not logged with its body: OpenAI's 401 body quotes part of the key it refused.
            if (clientResult.Status is 401 or 403)
            {
                logger.LogWarning("{Operation} AI call was refused: the key holder's {Provider} key was rejected (HTTP {Status}).",
                    operation, funding.Provider, clientResult.Status);
                return new AiKeyRejectedException(funding.Provider, clientResult);
            }

            // OpenAI says "out of credit" as a 429 whose error code is insufficient_quota (any other
            // 429 is a throttle and stays retryable), or, from the image endpoint, as a 400 whose
            // code is billing_hard_limit_reached.
            if (clientResult.Status == 402
                || (clientResult.Status == 429 && body.Contains("insufficient_quota", StringComparison.Ordinal))
                || (clientResult.Status == 400 && body.Contains("billing_hard_limit_reached", StringComparison.Ordinal)))
            {
                logger.LogWarning("{Operation} AI call was refused: the key holder's {Provider} account is out of credit.",
                    operation, funding.Provider);
                return new AiCreditExhaustedException(funding.Provider, clientResult);
            }
        }

        // The response body names the exact failure: for content_filter 400s it carries the
        // per-category verdicts the exception message omits. Without it the failure can't be
        // diagnosed.
        logger.LogError(clientResult,
            "{Operation} AI call failed with status {Status}. Response body: {ResponseBody}",
            operation, clientResult.Status, body);
        return new AiHttpException($"{operation} AI call failed: HTTP {clientResult.Status}", clientResult.Status, clientResult);
    }

    private static string RawResponseBody(ClientResultException ex)
    {
        try
        {
            return ex.GetRawResponse()?.Content?.ToString() ?? "(no response body)";
        }
        catch
        {
            return "(response body unavailable)";
        }
    }
}
