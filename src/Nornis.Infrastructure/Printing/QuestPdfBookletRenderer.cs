using Nornis.Application.Prep;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using UglyToad.PdfPig;

namespace Nornis.Infrastructure.Printing;

/// <summary>
/// Lays the booklet out as a PDF with QuestPDF (feature 34). Every layout rule lives here, and
/// nothing here knows Markdown or Adventures:
///
/// - The cover has a page to itself; every chapter starts a page, its title across both columns.
/// - Within a chapter the text flows down the left column, then the right, then the next page.
/// - A heading never ends a column: it moves on with the start of what follows it.
/// - A box, stat block or table row stays whole when it fits a column; a box longer than about
///   half a column flows on instead, so it never leaves a column empty.
/// - A table's header repeats; list markers sit inside the column.
/// </summary>
public sealed class QuestPdfBookletRenderer : IBookletPdfRenderer
{
    /// <summary>A box or stat block up to this many characters is kept whole: about half a column.</summary>
    internal const int KeepWholeMaxChars = 1_200;

    /// <summary>A stat block up to this many lines is kept whole.</summary>
    internal const int KeepWholeMaxStatLines = 24;

    /// <summary>A paragraph or list up to this many characters is kept whole, and kept with the heading above it.</summary>
    internal const int KeepWholeTextChars = 500;

    internal const string Serif = "Newsreader";
    internal const string Sans = "IBM Plex Sans";
    internal const string Mono = "IBM Plex Mono";
    internal const string Fallback = "DejaVu Sans";

    private static readonly string Ink = "#1C1F24";
    private static readonly string Accent = "#8B4A3C";
    private static readonly string Hairline = "#E3DED3";
    private static readonly string Muted = "#6B6F76";
    private static readonly string Parchment = "#F3EAD7";
    private static readonly string ParchmentEdge = "#D9C9A6";
    private static readonly string CodeShade = "#F4F2EE";

    private const float Margin = 0.6f * 72;
    private const float Gutter = 0.3f * 72;
    private const float BlockSpacing = 6;

    static QuestPdfBookletRenderer()
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // A character no font has (an emoji, say) draws as a blank rather than failing the print.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        var assembly = typeof(QuestPdfBookletRenderer).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Nornis.Printing.Fonts.", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            FontManager.RegisterFontFromStream(stream);
        }
    }

    public BookletPdf Render(Booklet booklet)
    {
        var bytes = Document.Create(document =>
            {
                document.Page(page => Cover(page, booklet.Cover));
                foreach (var chapter in booklet.Chapters)
                {
                    document.Page(page => Chapter(page, booklet.Cover.Title, chapter));
                }
            })
            .WithMetadata(new DocumentMetadata
            {
                Title = booklet.Cover.Title,
                Author = booklet.Cover.AuthorLine,
                Creator = "Nornis",
                Producer = "Nornis",
            })
            .GeneratePdf();

        using var pdf = PdfDocument.Open(bytes);
        return new BookletPdf(bytes, pdf.NumberOfPages);
    }

    // ---------------------------------------------------------------------- Pages --

    private static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.Letter);
        page.Margin(Margin);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(s => s.FontFamily(Serif, Fallback).FontSize(10).LineHeight(1.3f).FontColor(Ink));
    }

    private static void Cover(PageDescriptor page, BookletCover cover)
    {
        Setup(page);
        page.Content().PaddingTop(150).Column(col =>
        {
            col.Spacing(8);
            col.Item().AlignCenter().Text(cover.Title).FontSize(28).SemiBold().LineHeight(1.1f);
            if (cover.SystemLine is { } system)
            {
                col.Item().AlignCenter().Text(system).FontSize(13);
            }

            if (cover.LevelsLine is { } levels)
            {
                col.Item().AlignCenter().Text(levels).FontSize(12);
            }

            col.Item().AlignCenter().Text(cover.AuthorLine).FontSize(12);
            if (cover.Pitch is { } pitch)
            {
                col.Item().PaddingTop(16).PaddingHorizontal(48).AlignCenter().Text(pitch).FontSize(12).Italic();
            }

            col.Item().PaddingTop(24).PaddingHorizontal(120).LineHorizontal(1.5f).LineColor(Accent);
            col.Item().AlignCenter().Text(cover.CreditsLine.ToUpperInvariant())
                .FontFamily(Sans, Fallback).FontSize(8.5f).LetterSpacing(0.08f).FontColor(Muted);
        });
    }

    private static void Chapter(PageDescriptor page, string adventureTitle, BookletChapter chapter)
    {
        Setup(page);

        if (chapter.Title is { } title)
        {
            page.Header().ShowOnce().PaddingBottom(12).Column(header =>
            {
                header.Item().Text(title).FontSize(20).SemiBold().LineHeight(1.15f);
                header.Item().PaddingTop(4).LineHorizontal(1).LineColor(Accent);
            });
        }

        page.Content().MultiColumn(columns =>
        {
            columns.Columns(2);
            columns.Spacing(Gutter);
            columns.BalanceHeight(false);
            columns.Spacer().AlignCenter().LineVertical(0.5f).LineColor(Hairline);
            columns.Content().Column(col => Blocks(col, chapter.Blocks, BlockSpacing));
        });

        page.Footer().PaddingTop(8).AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).FontSize(8).FontColor(Muted));
            t.Span(adventureTitle + " · ");
            t.CurrentPageNumber();
        });
    }

    // --------------------------------------------------------------------- Blocks --

    /// <summary>
    /// Lays out a run of blocks. A heading is grouped with the headings straight after it and the
    /// block they introduce. A small block keeps the group whole; a large one flows, but the group
    /// needs room for the headings and the first unbreakable piece of that block before it starts,
    /// so a heading never ends a column alone.
    /// </summary>
    /// <remarks>
    /// QuestPDF's <c>EnsureSpace</c> only acts on content that doesn't fit entirely, so it guards
    /// the group (which holds the large block), never a heading by itself.
    /// </remarks>
    private static void Blocks(ColumnDescriptor col, IReadOnlyList<BookletBlock> blocks, float spacing)
    {
        col.Spacing(spacing);
        for (var i = 0; i < blocks.Count; i++)
        {
            if (!IsHeading(blocks[i]))
            {
                Block(col.Item(), blocks[i]);
                continue;
            }

            var group = new List<BookletBlock>();
            while (i < blocks.Count && IsHeading(blocks[i]))
            {
                group.Add(blocks[i++]);
            }

            var room = group.Count * HeadingRoomPt;
            var keepWhole = true;
            if (i < blocks.Count)
            {
                room += FirstPiecePt(blocks[i]);
                keepWhole = IsSmall(blocks[i]);
                group.Add(blocks[i]);
            }
            else
            {
                i--;
            }

            var item = col.Item().EnsureSpace(room);
            if (keepWhole)
            {
                item = item.PreventPageBreak();
            }

            item.Column(inner =>
            {
                inner.Spacing(spacing);
                foreach (var member in group)
                {
                    Block(inner.Item(), member);
                }
            });
        }
    }

    /// <summary>A heading's height with the spacing after it, at its largest size.</summary>
    internal const float HeadingRoomPt = 27;

    /// <summary>One line of body text.</summary>
    private const float LinePt = 13;

    /// <summary>Body text characters in a column's line, roughly.</summary>
    private const int LineChars = 52;

    /// <summary>
    /// The height of the first piece of a large block that can't split: what must fit under its
    /// heading for the heading not to be left alone. It is an estimate made before layout.
    /// </summary>
    internal static float FirstPiecePt(BookletBlock block) => block switch
    {
        BookletList { Items.Count: > 0 } list => IsSmallItem(list.Items[0])
            ? Math.Max(1, (Chars(list.Items[0].Blocks) + LineChars - 1) / LineChars) * LinePt
            : 2 * LinePt,
        BookletBox => 20 + 2 * LinePt,
        BookletTable => 3 * LinePt,
        BookletStatBlock => 14 + 2 * LinePt,
        BookletCode => 12 + 2 * LinePt,
        _ => 2 * LinePt,
    };

    private static bool IsSmallItem(BookletListItem item) => Chars(item.Blocks) <= KeepWholeTextChars;

    private static bool IsHeading(BookletBlock block) => block is BookletSectionHeading or BookletHeading;

    /// <summary>Whether a block is small enough to keep whole in one column. It is an estimate made before layout.</summary>
    internal static bool IsSmall(BookletBlock block) => block switch
    {
        BookletBox box => Chars(box.Blocks) <= KeepWholeMaxChars,
        BookletStatBlock stat => stat.Entries.Count <= KeepWholeMaxStatLines,
        BookletTable table => table.Rows.Count <= 4 && Chars([table]) <= KeepWholeTextChars,
        BookletCode code => code.Text.Length <= KeepWholeTextChars,
        _ => Chars([block]) <= KeepWholeTextChars,
    };

    internal static int Chars(IEnumerable<BookletBlock> blocks) => blocks.Sum(block => block switch
    {
        BookletParagraph p => Chars(p.Text),
        BookletHeading h => Chars(h.Text),
        BookletSectionHeading s => s.Text.Length,
        BookletList l => l.Items.Sum(item => Chars(item.Blocks)),
        BookletBox b => Chars(b.Blocks),
        BookletTable t => t.Header.Sum(Chars) + t.Rows.Sum(r => r.Sum(Chars)),
        BookletStatBlock s => s.Entries.Sum(e => (e.Label?.Length ?? 0) + e.Value.Length),
        BookletCode c => c.Text.Length,
        _ => 0,
    });

    private static int Chars(IReadOnlyList<BookletInline> text) => text.Sum(i => i is BookletRun r ? r.Text.Length : 1);

    private static void Block(IContainer container, BookletBlock block)
    {
        switch (block)
        {
            case BookletSectionHeading section:
                container.PaddingTop(6).Text(section.Text.ToUpperInvariant())
                    .FontFamily(Sans, Fallback).FontSize(10.5f).SemiBold().LetterSpacing(0.05f).FontColor(Accent);
                break;
            case BookletHeading heading:
                container.PaddingTop(2).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).SemiBold().FontSize(heading.Level switch
                    {
                        1 => 13,
                        2 => 12,
                        _ => 10.5f,
                    }));
                    Inlines(t, heading.Text);
                });
                break;
            case BookletParagraph paragraph:
                var whole = Chars(paragraph.Text) <= KeepWholeTextChars ? container.PreventPageBreak() : container;
                whole.Text(t => Inlines(t, paragraph.Text));
                break;
            case BookletList list:
                List(container, list);
                break;
            case BookletBox box:
                Box(IsSmall(box) ? container.PreventPageBreak() : container, box);
                break;
            case BookletStatBlock stat:
                StatBlock(IsSmall(stat) ? container.PreventPageBreak() : container, stat);
                break;
            case BookletTable table:
                Table(container, table);
                break;
            case BookletCode code:
                (IsSmall(code) ? container.PreventPageBreak() : container)
                    .Background(CodeShade).Padding(6)
                    .Text(code.Text).FontFamily(Mono, Fallback).FontSize(8.5f).LineHeight(1.2f);
                break;
            case BookletRule:
                container.PaddingVertical(4).LineHorizontal(0.5f).LineColor(Hairline);
                break;
        }
    }

    private static void List(IContainer container, BookletList list)
    {
        container.Column(col =>
        {
            col.Spacing(2);
            var number = list.Start;
            foreach (var item in list.Items)
            {
                var marker = list.Style == BookletListStyle.Numbered ? $"{number++}." : "•";
                var line = IsSmallItem(item) ? col.Item().PreventPageBreak() : col.Item();
                line.Row(row =>
                {
                    // The marker has its own cell inside the column, never in the gutter.
                    var markerCell = row.ConstantItem(item.Task != BookletTask.None ? 14 : list.Style == BookletListStyle.Numbered ? 16 : 10);
                    if (item.Task != BookletTask.None)
                    {
                        var box = markerCell.PaddingTop(3).Width(8).Height(8).Border(0.75f).BorderColor(Ink);
                        if (item.Task == BookletTask.Done)
                        {
                            box.AlignCenter().AlignMiddle().Text("✓").FontFamily(Sans, Fallback).FontSize(7).LineHeight(1);
                        }
                    }
                    else
                    {
                        markerCell.Text(marker);
                    }

                    row.RelativeItem().Column(inner => Blocks(inner, item.Blocks, 2));
                });
            }
        });
    }

    private static void Box(IContainer container, BookletBox box)
    {
        var framed = box.Kind switch
        {
            BookletBoxKind.Prop => container.Background(Parchment).Border(0.75f).BorderColor(ParchmentEdge).Padding(9),
            BookletBoxKind.Ask => container.BorderLeft(2).BorderColor(Ink).PaddingLeft(8).PaddingVertical(2),
            _ => container.Border(0.75f).BorderColor(Hairline).BorderLeft(3).BorderColor(Accent).Background(Colors.White)
                .PaddingVertical(7).PaddingHorizontal(9),
        };

        framed.Column(col =>
        {
            if (box.Kind == BookletBoxKind.ReadAloud)
            {
                col.Item().DefaultTextStyle(s => s.Italic()).Column(inner => Blocks(inner, box.Blocks, 4));
            }
            else if (box.Kind == BookletBoxKind.Ask && box.Blocks.Count > 0)
            {
                // A question's lead is bold, so it is found at a glance mid-session.
                col.Spacing(4);
                col.Item().DefaultTextStyle(s => s.SemiBold()).Column(inner => Blocks(inner, [box.Blocks[0]], 4));
                col.Item().Column(inner => Blocks(inner, box.Blocks.Skip(1).ToList(), 4));
            }
            else
            {
                col.Item().Column(inner => Blocks(inner, box.Blocks, 4));
            }
        });
    }

    private static void StatBlock(IContainer container, BookletStatBlock stat)
    {
        container.Border(1.5f).BorderColor(Ink).Padding(6)
            .DefaultTextStyle(s => s.FontFamily(Sans, Fallback).FontSize(8.5f).LineHeight(1.25f))
            .Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(62);
                    c.RelativeColumn();
                });

                foreach (var entry in stat.Entries)
                {
                    if (entry.Label is null)
                    {
                        table.Cell().ColumnSpan(2).PaddingVertical(1).Text(entry.Value);
                        continue;
                    }

                    table.Cell().PaddingVertical(1).PaddingRight(4).Text(entry.Label).SemiBold();
                    table.Cell().PaddingVertical(1).Text(entry.Value);
                }
            });
    }

    private static void Table(IContainer container, BookletTable table)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count));
        if (columns == 0)
        {
            return;
        }

        container.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).FontSize(8.5f).LineHeight(1.25f)).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                foreach (var width in ColumnWidths(table, columns))
                {
                    if (width.Fixed)
                    {
                        c.ConstantColumn(width.Value);
                    }
                    else
                    {
                        c.RelativeColumn(width.Value);
                    }
                }
            });

            if (table.Header.Count > 0)
            {
                t.Header(header =>
                {
                    for (var i = 0; i < columns; i++)
                    {
                        var text = i < table.Header.Count ? table.Header[i] : [];
                        header.Cell().BorderBottom(1).BorderColor(Ink).Padding(3)
                            .Text(x =>
                            {
                                x.DefaultTextStyle(s => s.SemiBold());
                                Inlines(x, text);
                            });
                    }
                });
            }

            foreach (var row in table.Rows)
            {
                for (var i = 0; i < columns; i++)
                {
                    var text = i < row.Count ? row[i] : [];
                    // A row never splits: each cell stays whole, so the row moves on together.
                    t.Cell().ShowEntire().BorderBottom(0.5f).BorderColor(Hairline).Padding(3).Text(x => Inlines(x, text));
                }
            }
        });
    }

    /// <summary>A table cell's characters are about this wide at the table's 8.5 pt.</summary>
    private const float TableCharPt = 4.7f;

    /// <summary>A column whose every cell is this short is sized to fit it, so "0:00-0:20" never wraps.</summary>
    internal const int ShortColumnChars = 16;

    internal readonly record struct ColumnWidth(bool Fixed, float Value);

    /// <summary>
    /// A short column (times, names, a DC) is a fixed width that fits its longest cell. The rest
    /// share what is left, wider for longer text, between one and three times the narrowest, and
    /// none narrower than its longest word, so a word never breaks across lines. If the fixed
    /// widths would take more than half the table, every column shares instead.
    /// </summary>
    internal static ColumnWidth[] ColumnWidths(BookletTable table, int columns)
    {
        var longest = new int[columns];
        var longestWord = new int[columns];
        var total = new float[columns];
        for (var i = 0; i < columns; i++)
        {
            var cells = table.Rows.Where(r => i < r.Count).Select(r => r[i]).ToList();
            var headerWord = i < table.Header.Count ? LongestWord(table.Header[i]) : 0;
            longest[i] = Math.Max(headerWord, cells.Select(Chars).DefaultIfEmpty(0).Max());
            longestWord[i] = Math.Max(headerWord, cells.Select(LongestWord).DefaultIfEmpty(0).Max());
            total[i] = cells.Sum(Chars) + headerWord;
        }

        var available = (PageSizes.Letter.Width - 2 * Margin - Gutter) / 2;
        var widths = new float?[columns];
        var shortWidths = longest.Select(l => l <= ShortColumnChars ? Fit(l) : 0f).ToArray();
        if (shortWidths.Any(w => w > 0) && shortWidths.Any(w => w == 0) && shortWidths.Sum() <= available / 2)
        {
            for (var i = 0; i < columns; i++)
            {
                widths[i] = shortWidths[i] > 0 ? shortWidths[i] : null;
            }
        }

        // Share the rest by weight; a column whose share is narrower than its longest word is
        // fixed at that word's width, and the others share again.
        while (true)
        {
            var shared = Enumerable.Range(0, columns).Where(i => widths[i] is null).ToList();
            if (shared.Count == 0)
            {
                break;
            }

            var shortest = Math.Max(1, shared.Select(i => total[i]).Where(l => l > 0).DefaultIfEmpty(1).Min());
            var weights = shared.ToDictionary(i => i, i => Math.Clamp(total[i] / shortest, 1, 3));
            var left = available - widths.Sum(w => w ?? 0);
            var squeezed = shared.Where(i => left * weights[i] / weights.Values.Sum() < Fit(longestWord[i])).ToList();
            if (squeezed.Count == 0 || squeezed.Count == shared.Count)
            {
                return Enumerable.Range(0, columns)
                    .Select(i => widths[i] is { } w ? new ColumnWidth(true, w) : new ColumnWidth(false, weights[i]))
                    .ToArray();
            }

            foreach (var i in squeezed)
            {
                widths[i] = Fit(longestWord[i]);
            }
        }

        return widths.Select(w => new ColumnWidth(true, w!.Value)).ToArray();
    }

    private static float Fit(int chars) => chars * TableCharPt + 8;

    private static int LongestWord(IReadOnlyList<BookletInline> text) =>
        string.Concat(text.OfType<BookletRun>().Select(r => r.Text))
            .Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length).DefaultIfEmpty(0).Max();

    // -------------------------------------------------------------------- Inlines --

    private static void Inlines(TextDescriptor text, IReadOnlyList<BookletInline> inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is BookletBreak)
            {
                text.Span("\n");
                continue;
            }

            var run = (BookletRun)inline;
            var span = text.Span(run.Text);
            if (run.Style.HasFlag(BookletRunStyle.Bold))
            {
                span.SemiBold();
            }

            if (run.Style.HasFlag(BookletRunStyle.Italic))
            {
                span.Italic();
            }

            if (run.Style.HasFlag(BookletRunStyle.Strike))
            {
                span.Strikethrough();
            }

            if (run.Style.HasFlag(BookletRunStyle.Code))
            {
                span.FontFamily(Mono, Fallback).FontSize(8.5f);
            }
        }
    }
}
