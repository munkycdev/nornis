using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;

namespace Nornis.Application.Prep;

/// <summary>
/// Builds the booklet's structure from an Adventure (feature 34). This is the one place
/// <see cref="PrepMarkup"/>'s print conventions are read, as <c>AdventurePrintModel</c> was before it:
///
/// - A plain blockquote is read-aloud; a <c>[!PROP]</c> or <c>[!ASK]</c> quote is a prop or a
///   question, the marker removed.
/// - A <c>statblock</c> or <c>item</c> fence is a game block, read by <see cref="GameBlocks"/>.
/// - A Chapter starts a <see cref="BookletChapter"/>; every other section is a heading in its
///   chapter's flow, and an unwritten one is left out.
/// - Every newline the GM typed is a line break, as in the editor (the Web's
///   <c>MarkdownRenderer</c>; <c>NewlineConventionMirrorTests</c> keeps the two in step).
/// - Raw HTML stays text, an image prints its alt text, and a link its text: nothing here
///   fetches or passes through markup.
/// </summary>
public static class BookletModel
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras()
        .UseTaskLists()
        .UseSoftlineBreakAsHardlineBreak()
        .DisableHtml()
        .Build();

    public static Booklet Build(Adventure adventure, PrintDetails details, BookletImprint imprint)
    {
        var chapters = new List<BookletChapter>();
        string? title = null;
        var blocks = new List<BookletBlock>();
        var open = false;

        foreach (var section in adventure.Sections.OrderBy(s => s.SortOrder))
        {
            if (section.Kind == AdventureSectionKind.Chapter)
            {
                if (open)
                {
                    chapters.Add(new BookletChapter(title, blocks));
                }

                title = section.Title;
                blocks = [.. Blocks(section.BodyMarkdown)];
                open = true;
                continue;
            }

            // An unwritten section has nothing to print; a Chapter is its own heading.
            if (AdventureSkeleton.IsUnfilled(section.BodyMarkdown))
            {
                continue;
            }

            open = true;
            blocks.Add(new BookletSectionHeading(section.Title, section.Kind));
            blocks.AddRange(Blocks(section.BodyMarkdown));
        }

        if (open)
        {
            chapters.Add(new BookletChapter(title, blocks));
        }

        return new Booklet(Cover(adventure, details), Rights(details, imprint), chapters);
    }

    /// <summary>The cover's lines (feature 32): system and edition, levels, author and pitch.</summary>
    private static BookletCover Cover(Adventure adventure, PrintDetails details) => new(
        adventure.Title,
        details.SystemLine(),
        LevelsLine(details),
        $"By {details.Author}",
        string.IsNullOrWhiteSpace(adventure.Pitch) ? null : adventure.Pitch.Trim());

    /// <summary>
    /// The rights page (feature 38). The copyright year is the book's year when the GM gave one,
    /// else the year it was printed. AI use comes from the resolved details, so the page and the
    /// filed snapshot can't disagree.
    /// </summary>
    private static BookletRights Rights(PrintDetails details, BookletImprint imprint)
    {
        var credits = new[] { details.Type, details.Publisher, details.Year?.ToString(CultureInfo.InvariantCulture) }
            .Where(v => !string.IsNullOrWhiteSpace(v));
        var year = details.Year ?? imprint.Printed.Year;

        return new BookletRights(
            details.Title,
            details.Publisher,
            string.Join(" · ", credits),
            $"© {year.ToString(CultureInfo.InvariantCulture)} {details.Author}",
            details.SystemLine(),
            LevelsLine(details),
            $"Printed {imprint.Printed.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}",
            $"Made with Nornis {imprint.Version}",
            RightsStatement.For(details.AiUse ?? AiUseKind.None),
            RightsStatement.Notice);
    }

    private static string? LevelsLine(PrintDetails details) =>
        string.IsNullOrWhiteSpace(details.Levels) ? null : $"Levels {details.Levels}";

    /// <summary>One section's Markdown as booklet blocks. Public for the tests that pin each convention.</summary>
    public static IReadOnlyList<BookletBlock> Blocks(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var document = Markdown.Parse(markdown.Replace("\r\n", "\n"), Pipeline);
        return Convert(document);
    }

    private static List<BookletBlock> Convert(ContainerBlock container)
    {
        var blocks = new List<BookletBlock>();
        foreach (var block in container)
        {
            if (Convert(block) is { } converted)
            {
                blocks.Add(converted);
            }
        }

        return blocks;
    }

    private static BookletBlock? Convert(Block block) => block switch
    {
        HeadingBlock heading => new BookletHeading(heading.Level, Inlines(heading.Inline)),
        ParagraphBlock paragraph => Paragraph(paragraph),
        ListBlock list => List(list),
        QuoteBlock quote => Box(quote),
        Table table => TableOf(table),
        FencedCodeBlock fence when GameBlocks.Parse(fence) is { } game => new BookletGameBlock(game),
        CodeBlock code => new BookletCode(code.Lines.ToString().TrimEnd('\n')),
        ThematicBreakBlock => new BookletRule(),
        ContainerBlock => null,
        LeafBlock leaf when leaf.Lines.Count > 0 => new BookletParagraph([new BookletRun(leaf.Lines.ToString())]),
        _ => null,
    };

    private static BookletParagraph? Paragraph(ParagraphBlock paragraph)
    {
        var text = Inlines(paragraph.Inline);
        return text.Count == 0 ? null : new BookletParagraph(text);
    }

    private static BookletList List(ListBlock list)
    {
        var items = new List<BookletListItem>();
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var task = BookletTask.None;
            if (item.Count > 0 && item[0] is ParagraphBlock { Inline: { } inline }
                && inline.FirstChild is TaskList marker)
            {
                task = marker.Checked ? BookletTask.Done : BookletTask.Open;
                marker.Remove();
                if (inline.FirstChild is LiteralInline literal)
                {
                    literal.Content = new Markdig.Helpers.StringSlice(literal.Content.ToString().TrimStart());
                }
            }

            items.Add(new BookletListItem(task, Convert(item)));
        }

        var start = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 1;
        return new BookletList(list.IsOrdered ? BookletListStyle.Numbered : BookletListStyle.Bullet, start, items);
    }

    private static BookletTable TableOf(Table table)
    {
        var header = new List<IReadOnlyList<BookletInline>>();
        var rows = new List<IReadOnlyList<IReadOnlyList<BookletInline>>>();
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = row.OfType<TableCell>().Select(CellText).ToList();
            if (row.IsHeader && header.Count == 0)
            {
                header.AddRange(cells);
            }
            else
            {
                rows.Add(cells);
            }
        }

        return new BookletTable(header, rows);
    }

    private static IReadOnlyList<BookletInline> CellText(TableCell cell)
    {
        var text = new List<BookletInline>();
        foreach (var paragraph in cell.OfType<ParagraphBlock>())
        {
            if (text.Count > 0)
            {
                text.Add(new BookletBreak());
            }

            text.AddRange(Inlines(paragraph.Inline));
        }

        return text;
    }

    // --------------------------------------------------------------------- Boxes --

    private static BookletBox Box(QuoteBlock quote)
    {
        var kind = MarkQuote(quote);
        return new BookletBox(kind, Convert(quote));
    }

    /// <summary>
    /// Finds a marker on a quote's first line, removes the marker line so it never prints, and
    /// says which box it is. The editor writes the marker and its text as one paragraph split by
    /// a line break; a marker alone in its paragraph is removed with that paragraph.
    /// </summary>
    private static BookletBoxKind MarkQuote(QuoteBlock quote)
    {
        if (quote.Count == 0 || quote[0] is not ParagraphBlock { Inline: { } inline } first)
        {
            return BookletBoxKind.ReadAloud;
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

        BookletBoxKind? kind = firstLine.ToString().Trim() switch
        {
            PrepMarkup.PropMarker => BookletBoxKind.Prop,
            PrepMarkup.AskMarker => BookletBoxKind.Ask,
            PrepMarkup.GmMarker => BookletBoxKind.Gm,
            _ => null,
        };
        if (kind is null)
        {
            return BookletBoxKind.ReadAloud;
        }

        if (lineBreak is null)
        {
            quote.Remove(first);
            return kind.Value;
        }

        foreach (var node in lineInlines)
        {
            node.Remove();
        }

        lineBreak.Remove();
        return kind.Value;
    }

    private static string TextOf(Inline node) => node switch
    {
        LiteralInline literal => literal.Content.ToString(),
        ContainerInline container => string.Concat(container.Select(TextOf)),
        _ => string.Empty,
    };

    // ------------------------------------------------------------------- Inlines --

    private static List<BookletInline> Inlines(ContainerInline? container)
    {
        var runs = new List<BookletInline>();
        if (container is not null)
        {
            Collect(container, BookletRunStyle.None, runs);
        }

        // A box whose marker line was removed can start with the break that followed it.
        while (runs.Count > 0 && runs[0] is BookletBreak)
        {
            runs.RemoveAt(0);
        }

        return runs;
    }

    private static void Collect(ContainerInline container, BookletRunStyle style, List<BookletInline> runs)
    {
        foreach (var node in container)
        {
            switch (node)
            {
                case LiteralInline literal:
                    Add(runs, literal.Content.ToString(), style);
                    break;
                case CodeInline code:
                    Add(runs, code.Content, style | BookletRunStyle.Code);
                    break;
                case LineBreakInline lineBreak:
                    if (lineBreak.IsHard)
                    {
                        runs.Add(new BookletBreak());
                    }
                    else
                    {
                        Add(runs, " ", style);
                    }

                    break;
                case HtmlEntityInline entity:
                    Add(runs, entity.Transcoded.ToString(), style);
                    break;
                case AutolinkInline autolink:
                    Add(runs, autolink.Url, style);
                    break;
                case LinkInline link:
                    // An image prints its alt text, a link its text; no URL is ever fetched.
                    Collect(link, style, runs);
                    break;
                case EmphasisInline emphasis:
                    Collect(emphasis, style | EmphasisStyle(emphasis), runs);
                    break;
                case TaskList:
                    break;
                case ContainerInline nested:
                    Collect(nested, style, runs);
                    break;
                default:
                    break;
            }
        }
    }

    private static BookletRunStyle EmphasisStyle(EmphasisInline emphasis) => emphasis.DelimiterChar switch
    {
        '*' or '_' => emphasis.DelimiterCount >= 2 ? BookletRunStyle.Bold : BookletRunStyle.Italic,
        '~' when emphasis.DelimiterCount >= 2 => BookletRunStyle.Strike,
        _ => BookletRunStyle.None,
    };

    /// <summary>Adds text, joining it to the previous run when the style matches, so a line is a few runs, not dozens.</summary>
    private static void Add(List<BookletInline> runs, string text, BookletRunStyle style)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (runs.Count > 0 && runs[^1] is BookletRun last && last.Style == style)
        {
            runs[^1] = last with { Text = last.Text + text };
            return;
        }

        runs.Add(new BookletRun(text, style));
    }
}
