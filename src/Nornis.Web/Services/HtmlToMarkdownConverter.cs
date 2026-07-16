using System.Text;
using System.Text.RegularExpressions;

namespace Nornis.Web.Services;

/// <summary>
/// Converts TipTap editor HTML to Markdown for storage in <c>Source.Body</c>. The body is fed
/// verbatim into the extraction prompt and rendered as markdown on the source detail page, so
/// storage must be markdown, never HTML. Pure text transformation with no external dependencies
/// (ported from Chronicis's export converter, plus strikethrough and table support).
/// </summary>
public static partial class HtmlToMarkdownConverter
{
    // ── Compiled regex patterns ──────────────────────────────────────────────

    [GeneratedRegex(@"<strong[^>]*>(.*?)</strong>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StrongTag();

    [GeneratedRegex(@"<b[^>]*>(.*?)</b>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex BoldTag();

    [GeneratedRegex(@"<em[^>]*>(.*?)</em>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex EmTag();

    [GeneratedRegex(@"<i[^>]*>(.*?)</i>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ItalicTag();

    [GeneratedRegex(@"<(?:s|del|strike)[^>]*>(.*?)</(?:s|del|strike)>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StrikeTag();

    [GeneratedRegex(@"<a[^>]*href=""([^""]*)""[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorTag();

    [GeneratedRegex(@"<pre[^>]*><code[^>]*>([\s\S]*?)</code></pre>", RegexOptions.IgnoreCase)]
    private static partial Regex PreCodeBlock();

    [GeneratedRegex(@"<code[^>]*>(.*?)</code>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"<blockquote[^>]*>([\s\S]*?)</blockquote>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockquoteTag();

    [GeneratedRegex(@"<p[^>]*>(.*?)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ParagraphTag();

    [GeneratedRegex(@"<ul[^>]*>([\s\S]*)</ul>", RegexOptions.IgnoreCase)]
    private static partial Regex UnorderedList();

    [GeneratedRegex(@"<ol[^>]*>([\s\S]*)</ol>", RegexOptions.IgnoreCase)]
    private static partial Regex OrderedList();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemOpen();

    [GeneratedRegex(@"</li>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemClose();

    [GeneratedRegex(@"(<[uo]l[^>]*>[\s\S]*</[uo]l>)", RegexOptions.IgnoreCase)]
    private static partial Regex NestedList();

    [GeneratedRegex(@"<table[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase)]
    private static partial Regex TableTag();

    [GeneratedRegex(@"<tr[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase)]
    private static partial Regex TableRowTag();

    [GeneratedRegex(@"<t[hd][^>]*>([\s\S]*?)</t[hd]>", RegexOptions.IgnoreCase)]
    private static partial Regex TableCellTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();

    [GeneratedRegex(@"<hr[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExcessiveNewlines();

    /// <summary>Header patterns for h1–h6. Index 0 = h1, index 5 = h6.</summary>
    private static readonly Regex[] HeaderPatterns =
    [
        H1Regex(), H2Regex(), H3Regex(), H4Regex(), H5Regex(), H6Regex(),
    ];

    [GeneratedRegex(@"<h1[^>]*>(.*?)</h1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H1Regex();

    [GeneratedRegex(@"<h2[^>]*>(.*?)</h2>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H2Regex();

    [GeneratedRegex(@"<h3[^>]*>(.*?)</h3>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H3Regex();

    [GeneratedRegex(@"<h4[^>]*>(.*?)</h4>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H4Regex();

    [GeneratedRegex(@"<h5[^>]*>(.*?)</h5>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H5Regex();

    [GeneratedRegex(@"<h6[^>]*>(.*?)</h6>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex H6Regex();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Converts HTML content to Markdown. Returns an empty string for blank input.</summary>
    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var markdown = html;

        // Tables first: cells keep their inline tags, which the later passes convert in place.
        markdown = ConvertTables(markdown);
        markdown = ConvertHeaders(markdown);
        markdown = ConvertInlineFormatting(markdown);
        markdown = ConvertLinks(markdown);
        markdown = ConvertCodeBlocks(markdown);
        markdown = ConvertBlockquotes(markdown);
        markdown = ConvertLists(markdown);
        markdown = ConvertParagraphsAndBreaks(markdown);
        markdown = StripRemainingTags(markdown);
        markdown = System.Net.WebUtility.HtmlDecode(markdown);
        markdown = NormalizeWhitespace(markdown);

        return markdown;
    }

    // ── Tables ──────────────────────────────────────────────

    internal static string ConvertTables(string html)
    {
        return TableTag().Replace(html, m =>
        {
            var rows = TableRowTag().Matches(m.Groups[1].Value);
            if (rows.Count == 0)
                return "\n";

            var sb = new StringBuilder();
            sb.Append('\n');
            var headerEmitted = false;

            foreach (Match row in rows)
            {
                var cells = TableCellTag().Matches(row.Groups[1].Value)
                    .Select(c => CleanTableCell(c.Groups[1].Value))
                    .ToList();
                if (cells.Count == 0)
                    continue;

                sb.Append("| ").Append(string.Join(" | ", cells)).Append(" |\n");

                // The first row becomes the markdown header (TipTap inserts tables with a header row).
                if (!headerEmitted)
                {
                    sb.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", cells.Count))).Append('\n');
                    headerEmitted = true;
                }
            }

            sb.Append('\n');
            return sb.ToString();
        });
    }

    private static string CleanTableCell(string cellHtml)
    {
        // Cell content must stay on one line; inline marks survive for the later global passes.
        var text = ParagraphTag().Replace(cellHtml, "$1 ");
        text = BreakTag().Replace(text, " ");
        text = text.Replace('\n', ' ').Replace('\r', ' ');
        text = text.Replace("|", "\\|");
        return text.Trim();
    }

    // ── Headers ─────────────────────────────────────────────

    internal static string ConvertHeaders(string html)
    {
        var result = html;
        for (int i = 0; i < 6; i++)
        {
            var prefix = new string('#', i + 1);
            result = HeaderPatterns[i].Replace(result, $"{prefix} $1\n\n");
        }
        return result;
    }

    // ── Inline Formatting ───────────────────────────────────

    internal static string ConvertInlineFormatting(string html)
    {
        var result = StrongTag().Replace(html, "**$1**");
        result = BoldTag().Replace(result, "**$1**");
        result = EmTag().Replace(result, "*$1*");
        result = ItalicTag().Replace(result, "*$1*");
        return StrikeTag().Replace(result, "~~$1~~");
    }

    // ── Links ───────────────────────────────────────────────

    internal static string ConvertLinks(string html)
        => AnchorTag().Replace(html, "[$2]($1)");

    // ── Code ────────────────────────────────────────────────

    internal static string ConvertCodeBlocks(string html)
    {
        var result = PreCodeBlock().Replace(html, "```\n$1\n```\n\n");
        return InlineCode().Replace(result, "`$1`");
    }

    // ── Blockquotes ─────────────────────────────────────────

    internal static string ConvertBlockquotes(string html)
    {
        return BlockquoteTag().Replace(html, m =>
        {
            var content = m.Groups[1].Value;
            content = ParagraphTag().Replace(content, "$1");
            var lines = content.Split('\n')
                .Select(l => "> " + l.Trim())
                .Where(l => l != "> ");
            return string.Join("\n", lines) + "\n\n";
        });
    }

    // ── Lists ───────────────────────────────────────────────

    internal static string ConvertLists(string html)
    {
        var result = html;
        var previous = "";

        // Keep processing until no more changes (handles deep nesting)
        while (result != previous)
        {
            previous = result;

            // Greedy match captures the outermost list first;
            // ProcessList handles nested <ul>/<ol> recursively within each <li>.
            result = UnorderedList().Replace(result,
                m => ProcessList(m.Groups[1].Value, ordered: false, indentLevel: 0));
            result = OrderedList().Replace(result,
                m => ProcessList(m.Groups[1].Value, ordered: true, indentLevel: 0));
        }

        return result;
    }

    internal static string ProcessList(string listContent, bool ordered, int indentLevel)
    {
        var sb = new StringBuilder();
        var indent = new string(' ', indentLevel * 2);
        var counter = 1;

        var items = ExtractListItems(listContent);

        foreach (var itemContent in items)
        {
            var (textContent, nestedListHtml) = SplitNestedList(itemContent);

            textContent = StripInlineTags(textContent);

            var prefix = ordered ? $"{counter}. " : "- ";
            sb.AppendLine($"{indent}{prefix}{textContent}");
            counter++;

            if (!string.IsNullOrEmpty(nestedListHtml))
            {
                sb.Append(RenderNestedList(nestedListHtml, indentLevel + 1));
            }
        }

        if (indentLevel == 0)
            sb.AppendLine();

        return sb.ToString();
    }

    private static List<string> ExtractListItems(string listContent)
    {
        var items = new List<string>();
        var liOpens = ListItemOpen().Matches(listContent);
        var consumedUntil = 0;

        foreach (Match openMatch in liOpens)
        {
            // Skip <li> tags that live inside an item already extracted — they belong to a
            // nested list and are rendered recursively, not as top-level items.
            if (openMatch.Index < consumedUntil)
                continue;

            var start = openMatch.Index + openMatch.Length;
            var depth = 1;
            var pos = start;

            while (pos < listContent.Length && depth > 0)
            {
                var nextOpen = ListItemOpen().Match(listContent[pos..]);
                var nextClose = ListItemClose().Match(listContent[pos..]);

                if (!nextClose.Success)
                    break;

                if (nextOpen.Success && nextOpen.Index < nextClose.Index)
                {
                    depth++;
                    pos += nextOpen.Index + nextOpen.Length;
                }
                else
                {
                    depth--;
                    if (depth == 0)
                    {
                        items.Add(listContent[start..(pos + nextClose.Index)]);
                        consumedUntil = pos + nextClose.Index + nextClose.Length;
                    }
                    pos += nextClose.Index + nextClose.Length;
                }
            }
        }

        return items;
    }

    private static (string text, string nestedHtml) SplitNestedList(string itemContent)
    {
        var nestedMatch = NestedList().Match(itemContent);
        if (!nestedMatch.Success)
            return (itemContent, "");

        var text = itemContent[..nestedMatch.Index];
        return (text, nestedMatch.Groups[1].Value);
    }

    private static string RenderNestedList(string nestedHtml, int indentLevel)
    {
        var sb = new StringBuilder();

        var ulMatch = UnorderedList().Match(nestedHtml);
        if (ulMatch.Success)
            sb.Append(ProcessList(ulMatch.Groups[1].Value, ordered: false, indentLevel));

        var olMatch = OrderedList().Match(nestedHtml);
        if (olMatch.Success)
            sb.Append(ProcessList(olMatch.Groups[1].Value, ordered: true, indentLevel));

        return sb.ToString();
    }

    private static string StripInlineTags(string text)
    {
        var result = ParagraphTag().Replace(text, "$1");
        return AnyTag().Replace(result, "").Trim();
    }

    // ── Paragraphs & Breaks ─────────────────────────────────

    internal static string ConvertParagraphsAndBreaks(string html)
    {
        var result = ParagraphTag().Replace(html, "$1\n\n");
        result = BreakTag().Replace(result, "\n");
        return HorizontalRule().Replace(result, "\n---\n\n");
    }

    // ── Cleanup ─────────────────────────────────────────────

    internal static string StripRemainingTags(string html)
        => AnyTag().Replace(html, "");

    internal static string NormalizeWhitespace(string text)
        // \n throughout — AppendLine emits \r\n on Windows and stored markdown must be stable.
        => ExcessiveNewlines().Replace(text.Replace("\r\n", "\n").Replace('\r', '\n'), "\n\n").Trim();
}
