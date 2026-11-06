namespace Nornis.Domain.Models;

/// <summary>
/// A page a research answer drew on (feature 40), as its provider reported it. Called a web page,
/// never a source or a reference: <c>Source</c> is the domain's word for an input to the world
/// record, and reference passages are the Library's.
/// </summary>
public sealed record WebPage(string Title, string Url);

/// <summary>
/// What a research turn of Prep's side conversation found (feature 40): kept with its answer,
/// shown under it, never copied into the adventure. <see cref="Create"/> is the only way to make
/// one, and the only place its bounds are applied, so whatever a provider sends is held to them
/// before it is stored or shown.
///
/// On a message, null means the turn wasn't a research turn; a result with no pages means it
/// was, and the answer cited nothing.
/// </summary>
public sealed record WebResearchResult
{
    public const int MaxPages = 20;

    public const int MaxTitleLength = 300;

    public const int MaxUrlLength = 2_048;

    /// <summary>Gemini's search suggestions are a small rendered snippet; anything larger is dropped, not cut.</summary>
    public const int MaxSuggestionsHtmlLength = 32 * 1024;

    public IReadOnlyList<WebPage> Pages { get; init; } = [];

    /// <summary>Searches the provider ran. Null when it didn't say, which is not the same as none.</summary>
    public int? Searches { get; init; }

    /// <summary>The provider's own search suggestions, to be shown sandboxed with the answer
    /// where its terms ask for them (Gemini). Null otherwise.</summary>
    public string? SuggestionsHtml { get; init; }

    /// <summary>
    /// The pages in the order given, each once (by URL), only absolute http or https URLs that
    /// fit, titles cut to fit (or the URL's host when there is none), at most
    /// <see cref="MaxPages"/>; a negative count as unreported; suggestions only when they fit.
    /// </summary>
    public static WebResearchResult Create(IEnumerable<WebPage> pages, int? searches, string? suggestionsHtml)
    {
        var kept = new List<WebPage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            if (kept.Count == MaxPages)
            {
                break;
            }

            var url = page.Url?.Trim() ?? string.Empty;
            if (url.Length > MaxUrlLength
                || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !seen.Add(url))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(page.Title) ? uri.Host : page.Title.Trim();
            kept.Add(new WebPage(title.Length > MaxTitleLength ? title[..MaxTitleLength] : title, url));
        }

        return new WebResearchResult
        {
            Pages = kept,
            Searches = searches is < 0 ? null : searches,
            SuggestionsHtml = string.IsNullOrWhiteSpace(suggestionsHtml) || suggestionsHtml.Length > MaxSuggestionsHtmlLength
                ? null
                : suggestionsHtml,
        };
    }
}
