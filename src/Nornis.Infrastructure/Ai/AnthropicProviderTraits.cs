using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Exceptions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Anthropic's Messages API through the official SDK's <see cref="IChatClient"/> adapter
/// (feature 39 Phase C). Only users' own keys reach it; Nornis has no Anthropic key of its own.
/// </summary>
public sealed class AnthropicProviderTraits : IAiProviderTraits
{
    public static readonly AnthropicProviderTraits Instance = new();

    private AnthropicProviderTraits()
    {
    }

    /// <summary>
    /// The adapter turns <see cref="ChatOptions.ResponseFormat"/> into the Messages API's
    /// <c>output_config.format</c> and <see cref="ChatOptions.MaxOutputTokens"/> into
    /// <c>max_tokens</c>, which Anthropic requires on every call: a call that names no ceiling gets
    /// the client's default (<see cref="UserKeyChatClients.AnthropicDefaultMaxTokens"/>). Our
    /// schemas are already in the strict dialect (<see cref="AiJsonSchema"/>), but Anthropic also
    /// caps how many union and optional properties a schema may have, so each one passes through
    /// <see cref="AnthropicSchemaLimits.Fit"/>, which leaves a schema under the caps as written.
    /// </summary>
    public ChatOptions? ChatOptions(AiCallOptions? options) => AdapterChatOptions(options, AnthropicSchemaLimits.Fit);

    /// <summary>The options for an SDK whose own <see cref="IChatClient"/> adapter reads
    /// <see cref="ChatOptions.ResponseFormat"/> and <see cref="ChatOptions.MaxOutputTokens"/>
    /// itself (Anthropic's and Google's); shared with <see cref="GeminiProviderTraits"/>.
    /// <paramref name="adaptSchema"/> is the provider's own change to a schema, if it needs one.</summary>
    internal static ChatOptions? AdapterChatOptions(AiCallOptions? options, Func<JsonNode, JsonNode>? adaptSchema = null)
    {
        if (options is null || (options.Schema is null && options.MaxOutputTokens is null))
        {
            return null;
        }

        var chatOptions = new ChatOptions { MaxOutputTokens = options.MaxOutputTokens };
        if (options.Schema is { } schema)
        {
            var node = JsonNode.Parse(schema.Schema)
                ?? throw new InvalidOperationException($"Structured-output schema '{schema.Name}' is empty.");
            if (adaptSchema is not null)
            {
                node = adaptSchema(node);
            }

            chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(JsonSerializer.SerializeToElement(node), schema.Name);
        }

        return chatOptions;
    }

    public AiHttpException? TranslateFailure(Exception ex, AiFunding funding, string operation, ILogger logger)
    {
        switch (ex)
        {
            case AnthropicApiException api:
                return FromStatus(api, (int)api.StatusCode, api.ResponseBody, funding, operation, logger);

            // A failure reported inside the stream rather than as the response status: Anthropic
            // sends "overloaded_error" this way mid-answer. Retryable, like its 529.
            case AnthropicSseException sse:
                logger.LogError(sse, "{Operation} AI call failed inside the stream.", operation);
                var status = sse.Message.Contains("overloaded", StringComparison.OrdinalIgnoreCase) ? 529 : 502;
                return new AiHttpException($"{operation} AI call failed: the stream reported an error", status, sse);

            case AnthropicIOException io:
                logger.LogError(io, "{Operation} AI call couldn't reach Anthropic.", operation);
                return new AiHttpException($"{operation} AI call couldn't reach the provider.", null, io);

            default:
                return null;
        }
    }

    private static AiHttpException FromStatus(
        Exception ex, int status, string body, AiFunding funding, string operation, ILogger logger)
    {
        if (funding.FundedBy == AiFundedBy.Key)
        {
            if (status is (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden)
            {
                logger.LogWarning("{Operation} AI call was refused: the key holder's {Provider} key was rejected (HTTP {Status}).",
                    operation, funding.Provider, status);
                return new AiKeyRejectedException(funding.Provider, ex);
            }

            // Anthropic reports an empty account as a 402 billing_error, and, on accounts that
            // predate it, as a 400 whose message says the credit balance is too low. A 429 is
            // always a throttle here, so it stays retryable.
            if (status == (int)HttpStatusCode.PaymentRequired
                || (status == (int)HttpStatusCode.BadRequest && body.Contains("credit balance", StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning("{Operation} AI call was refused: the key holder's {Provider} account is out of credit.",
                    operation, funding.Provider);
                return new AiCreditExhaustedException(funding.Provider, ex);
            }
        }

        // Anthropic's error bodies name the type and message and never echo the key.
        logger.LogError(ex, "{Operation} AI call failed with status {Status}. Response body: {ResponseBody}",
            operation, status, body);
        return new AiHttpException($"{operation} AI call failed: HTTP {status}", status, ex);
    }
}
