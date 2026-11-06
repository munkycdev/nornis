using System.Text.RegularExpressions;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

/// <summary>
/// The links a research answer wrote into its own text (feature 40, Requirement 3.3). The pages
/// are listed under the answer, so a Markdown link to one of them is kept as its link text only:
/// the answer reads cleanly and can go into a section as it stands. A link to anything else is
/// left alone, because it isn't one the list shows.
/// </summary>
public static partial class WebPageLinks
{
    /// <summary>
    /// <paramref name="text"/> with every <c>[text](url)</c> whose URL is one of
    /// <paramref name="pages"/> turned into <c>text</c>. URLs are matched without the tracking
    /// parameters (<c>utm_*</c>) OpenAI appends, so a citation of a page matches the page.
    /// </summary>
    public static string Strip(string text, IReadOnlyList<WebPage> pages)
    {
        if (pages.Count == 0 || !text.Contains("](", StringComparison.Ordinal))
        {
            return text;
        }

        var known = pages.Select(p => Normalize(p.Url)).ToHashSet(StringComparer.Ordinal);
        return MarkdownLink().Replace(text, m => known.Contains(Normalize(m.Groups["url"].Value)) ? m.Groups["text"].Value : m.Value);
    }

    /// <summary>The URL without its <c>utm_*</c> parameters or a trailing slash.</summary>
    private static string Normalize(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url.Trim();
        }

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("utm_", StringComparison.OrdinalIgnoreCase));
        var query = string.Join('&', kept);
        var path = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return query.Length == 0 ? path : $"{path}?{query}";
    }

    /// <summary>An inline Markdown link, not an image: link text with no brackets in it, then an http(s) URL in parentheses, optionally in angle brackets or with a title.</summary>
    [GeneratedRegex("""(?<!!)\[(?<text>[^\[\]\n]+)\]\(<?(?<url>https?://[^\s()<>]+(?:\([^\s()<>]*\)[^\s()<>]*)*)>?(?:\s+"[^"\n]*")?\)""")]
    private static partial Regex MarkdownLink();
}
