using System.Globalization;
using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

/// <summary>
/// The booklet (feature 29, Requirement 7): an Adventure's sections as one HTML document in
/// reading order, with <see cref="PrepMarkup"/>'s conventions set as the print stylesheet
/// expects them. The one renderer: the API's print route returns this, and v3.1's worker will
/// print the same HTML to PDF.
///
/// - A plain blockquote is read-aloud: <c>&lt;blockquote class="read-aloud"&gt;</c>.
/// - A <c>[!PROP]</c> quote is an in-world document: <c>&lt;aside class="prop"&gt;</c>, marker removed.
/// - An <c>[!ASK]</c> quote is a question to the table: <c>&lt;aside class="ask"&gt;</c>, marker removed.
/// - A <c>statblock</c> fence is a framed grid: <c>&lt;dl class="statblock"&gt;</c> of label and value.
/// - A Chapter opens a new page (<c>class="booklet-chapter"</c>); every other section is an <c>h2</c>.
///
/// Raw HTML in a body is escaped, never passed through: the page shows this as markup, and a
/// section is text the GM or the model wrote.
/// </summary>
public static class AdventurePrintModel
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    /// <summary>
    /// The whole booklet. <paramref name="details"/> is what the cover prints under the title
    /// (feature 32): the game system and edition, the levels and the author, each only when set,
    /// and a credits line of type, publisher and year at the cover's foot.
    /// </summary>
    public static string Render(Adventure adventure, PrintDetails details)
    {
        var html = new StringBuilder();
        html.Append("<article class=\"booklet\">");
        html.Append("<header class=\"booklet-cover\"><h1 class=\"booklet-title\">")
            .Append(Encode(adventure.Title)).Append("</h1>");

        var system = string.Join(' ', new[] { details.GameSystem, details.Edition }.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (system.Length > 0)
        {
            html.Append("<p class=\"booklet-system\">").Append(Encode(system)).Append("</p>");
        }

        if (!string.IsNullOrWhiteSpace(details.Levels))
        {
            html.Append("<p class=\"booklet-levels\">Levels ").Append(Encode(details.Levels)).Append("</p>");
        }

        html.Append("<p class=\"booklet-author\">By ").Append(Encode(details.Author)).Append("</p>");

        if (!string.IsNullOrWhiteSpace(adventure.Pitch))
        {
            html.Append("<p class=\"booklet-pitch\">").Append(Encode(adventure.Pitch.Trim())).Append("</p>");
        }

        // Each part is encoded on its own, so the separator stays a literal middle dot.
        var credits = new[] { details.Type, details.Publisher, details.Year?.ToString(CultureInfo.InvariantCulture) }
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => Encode(v!));
        html.Append("<p class=\"booklet-credits\">").Append(string.Join(" · ", credits)).Append("</p>");

        html.Append("</header>");

        foreach (var section in adventure.Sections.OrderBy(s => s.SortOrder))
        {
            var isChapter = section.Kind == AdventureSectionKind.Chapter;

            // An unwritten section has nothing to print, but a Chapter is its own heading.
            if (!isChapter && AdventureSkeleton.IsUnfilled(section.BodyMarkdown))
            {
                continue;
            }

            var kindClass = $"booklet-{Kebab(section.Kind)}";
            html.Append(isChapter
                ? $"<section class=\"booklet-chapter {kindClass}\"><h1 class=\"booklet-chapter-title\">"
                : $"<section class=\"booklet-section {kindClass}\"><h2 class=\"booklet-section-title\">");
            html.Append(Encode(section.Title)).Append(isChapter ? "</h1>" : "</h2>");
            html.Append(RenderBody(section.BodyMarkdown));
            html.Append("</section>");
        }

        html.Append("</article>");
        return html.ToString();
    }

    /// <summary>One section's Markdown as booklet HTML. Public for the tests that pin each convention.</summary>
    public static string RenderBody(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var document = Markdown.Parse(markdown.Replace("\r\n", "\n"), Pipeline);
        foreach (var quote in document.Descendants<QuoteBlock>().ToList())
        {
            MarkQuote(quote);
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.Replace<QuoteBlockRenderer>(new PrepQuoteRenderer());
        renderer.ObjectRenderers.Replace<CodeBlockRenderer>(new PrepCodeBlockRenderer());
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private const string KindKey = "prep-kind";

    /// <summary>
    /// Finds a marker on a quote's first line, notes the kind on the block, and removes the marker
    /// line so it never prints. The editor writes the marker and its text as one paragraph split by
    /// a line break; a marker alone in its paragraph is removed with that paragraph.
    /// </summary>
    private static void MarkQuote(QuoteBlock quote)
    {
        if (quote.Count == 0 || quote[0] is not ParagraphBlock { Inline: { } inline } first)
        {
            return;
        }

        var lineInlines = new List<Inline>();
        var firstLine = new StringBuilder();
        Inline? lineBreak = null;
        foreach (var node in inline)
        {
            if (node is LineBreakInline)
            {
                lineBreak = node;
                break;
            }

            lineInlines.Add(node);
            firstLine.Append(TextOf(node));
        }

        var kind = firstLine.ToString().Trim() switch
        {
            PrepMarkup.PropMarker => "prop",
            PrepMarkup.AskMarker => "ask",
            _ => null,
        };
        if (kind is null)
        {
            return;
        }

        quote.SetData(KindKey, kind);
        if (lineBreak is null)
        {
            quote.Remove(first);
            return;
        }

        foreach (var node in lineInlines)
        {
            node.Remove();
        }

        lineBreak.Remove();
    }

    private static string TextOf(Inline node) => node switch
    {
        LiteralInline literal => literal.Content.ToString(),
        ContainerInline container => string.Concat(container.Select(TextOf)),
        _ => string.Empty,
    };

    private sealed class PrepQuoteRenderer : HtmlObjectRenderer<QuoteBlock>
    {
        protected override void Write(HtmlRenderer renderer, QuoteBlock block)
        {
            var kind = block.GetData(KindKey) as string;
            renderer.EnsureLine();
            renderer.Write(kind is null ? "<blockquote class=\"read-aloud\">" : $"<aside class=\"{kind}\">");
            renderer.WriteLine();
            var implicitParagraph = renderer.ImplicitParagraph;
            renderer.ImplicitParagraph = false;
            renderer.WriteChildren(block);
            renderer.ImplicitParagraph = implicitParagraph;
            renderer.WriteLine(kind is null ? "</blockquote>" : "</aside>");
        }
    }

    private sealed class PrepCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
    {
        private readonly CodeBlockRenderer _default = new();

        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            if (block is not FencedCodeBlock { Info: PrepMarkup.StatBlockInfo })
            {
                _default.Write(renderer, block);
                return;
            }

            renderer.EnsureLine();
            renderer.Write("<dl class=\"statblock\">");
            foreach (var line in block.Lines.Lines.Take(block.Lines.Count))
            {
                var text = line.Slice.ToString().Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                var colon = text.IndexOf(':');
                if (colon > 0)
                {
                    renderer.Write("<dt>").Write(Encode(text[..colon].Trim())).Write("</dt>");
                    renderer.Write("<dd>").Write(Encode(text[(colon + 1)..].Trim())).Write("</dd>");
                }
                else
                {
                    renderer.Write("<dd class=\"statblock-wide\">").Write(Encode(text)).Write("</dd>");
                }
            }

            renderer.WriteLine("</dl>");
        }
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);

    private static string Kebab(AdventureSectionKind kind)
    {
        var name = kind.ToString();
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0)
            {
                sb.Append('-');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }
}
