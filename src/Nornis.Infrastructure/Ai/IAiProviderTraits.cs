using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// What differs between AI providers once a call has an <see cref="IChatClient"/> (feature 39):
/// how a schema and an output ceiling are expressed, and how the provider's SDK reports failure.
/// Everything else (timeouts, usage, streaming) is the same shell in <see cref="AiCallExecutor"/>.
/// </summary>
public interface IAiProviderTraits
{
    /// <summary>The provider's options for <paramref name="options"/>; null when there is nothing to ask.</summary>
    ChatOptions? ChatOptions(AiCallOptions? options);

    /// <summary>
    /// The Application taxonomy's reading of a failure the provider's SDK raised, or null when
    /// <paramref name="ex"/> is not one of its exceptions (the executor then treats it as unknown).
    /// </summary>
    AiHttpException? TranslateFailure(Exception ex, string operation, ILogger logger);
}
