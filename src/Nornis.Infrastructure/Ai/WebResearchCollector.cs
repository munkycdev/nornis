using Nornis.Domain.Models;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// What a research call's stream reports about its searching (feature 40), gathered update by
/// update by the provider's <see cref="IAiProviderTraits.ReadWebResearch"/>. It only collects;
/// <see cref="WebResearchResult.Create"/> applies the bounds when the stream ends.
/// </summary>
public sealed class WebResearchCollector
{
    private readonly List<WebPage> _pages = [];

    public IReadOnlyList<WebPage> Pages => _pages;

    /// <summary>Null until the provider reports a count.</summary>
    public int? Searches { get; private set; }

    public string? SuggestionsHtml { get; set; }

    public void AddPage(string? title, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            _pages.Add(new WebPage(title ?? string.Empty, url));
        }
    }

    /// <summary>Adds to the count, for providers that report searches one call at a time.</summary>
    public void CountSearches(int searches) => Searches = (Searches ?? 0) + searches;

    /// <summary>Sets the count, for providers that report a total for the call.</summary>
    public void SetSearches(int searches) => Searches = searches;

    public WebResearchResult ToResult() => WebResearchResult.Create(_pages, Searches, SuggestionsHtml);
}
