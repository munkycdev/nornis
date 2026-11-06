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
    /// When a user's key paid (<paramref name="funding"/>), a refused key or an account out of
    /// credit becomes the matching <see cref="AiRefusalException"/>; with Nornis's own key those
    /// stay plain HTTP failures, because they are Nornis's to fix, not the GM's.
    /// </summary>
    AiHttpException? TranslateFailure(Exception ex, AiFunding funding, string operation, ILogger logger);

    /// <summary>
    /// Reads what one streamed update says about the call's web searching (feature 40): the pages
    /// the answer drew on and the searches run. Called only for a call that asked for
    /// <see cref="AiCallOptions.WebSearch"/>.
    /// </summary>
    void ReadWebResearch(ChatResponseUpdate update, WebResearchCollector into);

    /// <summary>
    /// Whether <paramref name="ex"/>, from a call that asked for web search, is the provider
    /// refusing the search tool for this model (feature 40, Requirement 5), rather than any other
    /// failure. Checked before <see cref="TranslateFailure"/>, and only for such a call.
    /// </summary>
    bool RefusedWebSearch(Exception ex);
}

/// <summary>What the providers' adapters report the same way, through Microsoft.Extensions.AI's own types.</summary>
internal static class WebSearchReading
{
    /// <summary>The pages cited in the answer's text: <see cref="CitationAnnotation"/>s carrying a URL.</summary>
    public static void AddCitedPages(ChatResponseUpdate update, WebResearchCollector into)
    {
        foreach (var content in update.Contents)
        {
            foreach (var annotation in content.Annotations ?? [])
            {
                if (annotation is CitationAnnotation { Url: { } url } citation)
                {
                    into.AddPage(citation.Title, url.ToString());
                }
            }
        }
    }
}
