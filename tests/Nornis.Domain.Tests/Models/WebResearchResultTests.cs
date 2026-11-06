using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Domain.Tests.Models;

/// <summary>Feature 40: whatever a provider reports about its searching is held to the bounds before it is kept.</summary>
[TestFixture]
public class WebResearchResultTests
{
    [Test]
    public void KeepsHttpPagesOnceInOrder_AndDropsTheRest()
    {
        var result = WebResearchResult.Create(
        [
            new WebPage("Waterdeep", "https://example.org/waterdeep"),
            new WebPage("Again", "https://example.org/waterdeep"),
            new WebPage("Script", "javascript:alert(1)"),
            new WebPage("Relative", "/waterdeep"),
            new WebPage("Old web", "http://example.net/deep"),
            new WebPage("Too long", "https://example.org/" + new string('a', WebResearchResult.MaxUrlLength)),
        ], searches: 3, suggestionsHtml: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Pages.Select(p => p.Url), Is.EqualTo(["https://example.org/waterdeep", "http://example.net/deep"]));
            Assert.That(result.Pages[0].Title, Is.EqualTo("Waterdeep"), "the first title for a URL wins");
            Assert.That(result.Searches, Is.EqualTo(3));
        });
    }

    [Test]
    public void CutsLongTitles_NamesUntitledPagesByHost_AndStopsAtTheCap()
    {
        var pages = Enumerable.Range(0, WebResearchResult.MaxPages + 5)
            .Select(i => new WebPage(i == 0 ? new string('t', 400) : i == 1 ? " " : $"Page {i}", $"https://example.org/{i}"));

        var result = WebResearchResult.Create(pages, searches: null, suggestionsHtml: null);

        Assert.Multiple(() =>
        {
            Assert.That(result.Pages, Has.Count.EqualTo(WebResearchResult.MaxPages));
            Assert.That(result.Pages[0].Title, Has.Length.EqualTo(WebResearchResult.MaxTitleLength));
            Assert.That(result.Pages[1].Title, Is.EqualTo("example.org"));
        });
    }

    [Test]
    public void NullAndNegativeCountsAreUnreported_ZeroIsNone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebResearchResult.Create([], null, null).Searches, Is.Null);
            Assert.That(WebResearchResult.Create([], -1, null).Searches, Is.Null);
            Assert.That(WebResearchResult.Create([], 0, null).Searches, Is.Zero);
        });
    }

    [Test]
    public void SuggestionsAreKeptOnlyWhenTheyFit()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebResearchResult.Create([], 1, "<div>chip</div>").SuggestionsHtml, Is.EqualTo("<div>chip</div>"));
            Assert.That(WebResearchResult.Create([], 1, new string('x', WebResearchResult.MaxSuggestionsHtmlLength + 1)).SuggestionsHtml,
                Is.Null, "cut HTML could render broken, so too much is dropped whole");
            Assert.That(WebResearchResult.Create([], 1, "  ").SuggestionsHtml, Is.Null);
        });
    }
}
