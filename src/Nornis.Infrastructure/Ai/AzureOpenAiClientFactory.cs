using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAI;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Builds the OpenAI client for an Azure OpenAI resource, with an explicit retry count, because the
/// SDK's default of three sits underneath every retry ladder this system designed and is invisible
/// to all of them.
///
/// Azure's v1 endpoint (<c>{resource}/openai/v1/</c>) speaks the OpenAI wire protocol, so the plain
/// OpenAI SDK talks to it directly and the deployment name goes where OpenAI puts the model name.
/// Azure.AI.OpenAI is not used: its last stable release pins an OpenAI SDK too old for
/// Microsoft.Extensions.AI, whose <c>IChatClient</c> every chat client here is written against.
/// </summary>
public static class AzureOpenAiClientFactory
{
    /// <summary>
    /// Retries owned entirely by the caller's own ladder. The worker's queue redelivery is the
    /// ladder: three SDK attempts inside each of five deliveries is fifteen calls to the model
    /// for what <c>RedeliveryBackoff</c> spaced out as five — and the near-instant re-request it
    /// was written to eliminate happens below its sight line, so its backoff never sees it.
    /// </summary>
    public const int RetriesOwnedByBackoff = 0;

    /// <summary>
    /// One retry, for a synchronous request with nothing behind it. A user-facing Ask has no
    /// redelivery to fall back on, so the multiplication that makes retries dangerous in the
    /// worker cannot happen here — and a single blip becomes an answer rather than an error.
    /// </summary>
    public const int RetriesForUserFacingCall = 1;

    /// <param name="resourceEndpoint">The resource root as configured, e.g.
    /// <c>https://name.openai.azure.com/</c>. The v1 path is appended here, once.</param>
    public static OpenAIClient Create(Uri resourceEndpoint, ApiKeyCredential credential, int maxRetries) =>
        new(credential, new OpenAIClientOptions
        {
            Endpoint = V1Endpoint(resourceEndpoint),
            RetryPolicy = new ClientRetryPolicy(maxRetries),
        });

    internal static Uri V1Endpoint(Uri resourceEndpoint) =>
        new(new Uri(resourceEndpoint.GetLeftPart(UriPartial.Authority) + "/"), "openai/v1/");
}
