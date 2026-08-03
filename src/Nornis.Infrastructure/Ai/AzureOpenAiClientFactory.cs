using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Builds the Azure OpenAI client with an explicit retry count, because the SDK's default of
/// three sits underneath every retry ladder this system designed and is invisible to all of them.
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

    public static AzureOpenAIClient Create(Uri endpoint, ApiKeyCredential credential, int maxRetries) =>
        new(endpoint, credential, new AzureOpenAIClientOptions
        {
            RetryPolicy = new ClientRetryPolicy(maxRetries),
        });
}
