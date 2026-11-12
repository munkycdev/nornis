using Nornis.Application.Prep;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
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
/// - The cover has a page to itself, and the rights page (feature 38) the next, under the Nornis
///   Press mark; every chapter starts a page, its title across both columns.
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

    internal const string Sans = "IBM Plex Sans";
    internal const string Mono = "IBM Plex Mono";
    internal const string Fallback = "DejaVu Sans";

    private const string Ink = PrintColors.Ink;
    private static readonly string Accent = "#8B4A3C";
    private static readonly string Hairline = "#E3DED3";
    private static readonly string Muted = "#6B6F76";
    private static readonly string Parchment = "#F3EAD7";
    private static readonly string ParchmentEdge = "#D9C9A6";
    private static readonly string CodeShade = "#F4F2EE";

    private const float Margin = 0.6f * 72;
    private const float Gutter = 0.3f * 72;
    private const float BlockSpacing = 6;

    /// <summary>The Nornis Press mark's height on the rights page: about three quarters of an inch.</summary>
    internal const float PressMarkSize = 54;

    /// <summary>The Nornis logo, drawn on the rights page as the imprint's mark.</summary>
    private static readonly byte[] PressMark = Resource("Nornis.Printing.Images.nornis-logo.png");

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

    private static byte[] Resource(string name)
    {
        using var stream = typeof(QuestPdfBookletRenderer).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource {name} is missing.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    public BookletPdf Render(Booklet booklet)
    {
        var handouts = booklet.Handouts ?? [];

        // The appendix (feature 38) is cut into runs: drawn pages (images and "couldn't print"
        // pages) render with the booklet, and each PDF handout is merged in after its place, so
        // the GM's order holds across both kinds.
        var runs = Runs(handouts);
        var type = Typeset.Of(booklet.Typography);
        var bytes = Document.Create(document =>
            {
                document.Page(page => Cover(page, booklet.Cover, type));
                document.Page(page => Rights(page, booklet.Rights, type));
                foreach (var chapter in booklet.Chapters)
                {
                    document.Page(page => Chapter(page, booklet.Cover.Title, chapter, type));
                }

                if (handouts.Count > 0)
                {
                    document.Page(page => HandoutsOpener(page, handouts, type));
                    DrawnHandouts(document, runs[0].Drawn, type);
                }
            })
            .WithMetadata(Metadata(booklet.Cover.Title, booklet.Cover.AuthorLine))
            .GeneratePdf();

        if (runs.Any(r => r.Pdf is not null))
        {
            bytes = Merge(bytes, runs, type);
        }

        using var pdf = PdfDocument.Open(bytes);
        return new BookletPdf(bytes, pdf.NumberOfPages);
    }

    /// <summary>
    /// The props sheet (feature 38): each prop alone on a page in large type, in its parchment
    /// frame, to cut out and hand over. No footer: what's on the page is what the players get.
    /// </summary>
    public BookletPdf RenderProps(BookletProps props)
    {
        var type = Typeset.Of(props.Typography);
        var bytes = Document.Create(document =>
            {
                foreach (var prop in props.Props)
                {
                    document.Page(page =>
                    {
                        Setup(page, type);
                        page.DefaultTextStyle(s => s.FontFamily(type.Face, Fallback).FontSize(16).LineHeight(1.35f).FontColor(Ink));
                        page.Content().AlignMiddle().Background(Parchment).Border(1).BorderColor(ParchmentEdge).Padding(28)
                            .Column(col => Blocks(col, prop.Blocks, 10, type));
                    });
                }
            })
            .WithMetadata(Metadata(props.Title + " props", null))
            .GeneratePdf();

        using var pdf = PdfDocument.Open(bytes);
        return new BookletPdf(bytes, pdf.NumberOfPages);
    }

    private static DocumentMetadata Metadata(string title, string? author) => new()
    {
        Title = title,
        Author = author,
        Creator = "Nornis",
        Producer = "Nornis",
    };

    // ---------------------------------------------------------------------- Pages --

    /// <summary>
    /// A page in the book's face at the default size: the cover, rights and handout pages keep
    /// their own sizes whatever the GM chose. A chapter page sets the body's size over it.
    /// </summary>
    private static void Setup(PageDescriptor page, Typeset type)
    {
        page.Size(PageSizes.Letter);
        page.Margin(Margin);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(s => s.FontFamily(type.Face, Fallback).FontSize(Typeset.DefaultSize).LineHeight(Typeset.DefaultLineHeight).FontColor(Ink));
    }

    private static void Cover(PageDescriptor page, BookletCover cover, Typeset type)
    {
        switch (cover.Art)
        {
            case { Layout: CoverLayout.FullBleed } art when CoverArtImage.Fit(art.Image, PageSizes.Letter.Width, PageSizes.Letter.Height) is { } image:
                FullBleedCover(page, cover, image, type);
                return;
            case { Layout: CoverLayout.Framed } art when CoverArtImage.Fit(art.Image, FrameArtWidth, FrameArtHeight) is { } image:
                FramedCover(page, cover, image, type);
                return;
            case { Layout: CoverLayout.ClassicModule } art when CoverArtImage.Fit(art.Image, ContentWidth, ModuleArtHeight) is { } image:
                ClassicModuleCover(page, cover, image, type);
                return;
            case { Layout: CoverLayout.ScrollBanner } art when CoverArtImage.Fit(art.Image, PageSizes.Letter.Width, PageSizes.Letter.Height) is { } image:
                ScrollBannerCover(page, cover, image, type);
                return;
        }

        TypesetCover(page, cover, type);
    }

    // Feature 38's layouts. Every box the art fills has a fixed size, so the art is cropped to it
    // before layout and the cover can never spill onto the rights page. Type never sits on the art:
    // it's on a solid band (full bleed) or beside it (framed, classic module), so those layouts
    // don't depend on the art's colours. The scroll banner (feature 46) is the one exception: its
    // title is on an opaque scroll, but its author line is set on the art in whichever colour the
    // art behind it reads best against (ArtContrast).
    private static readonly float ContentWidth = PageSizes.Letter.Width - 2 * Margin;
    private const float FrameWidth = 410;
    private const float FrameHeight = 500;
    private const float FramePadding = 6;
    private const float FrameArtWidth = FrameWidth - 2 * FramePadding;
    private const float FrameArtHeight = FrameHeight - 2 * FramePadding;
    private const float ModuleArtHeight = 400;

    /// <summary>The longest a title runs on a cover with art, in lines.</summary>
    internal const int ArtTitleLines = 3;

    /// <summary>The longest a pitch runs on the classic module cover, in lines.</summary>
    internal const int ModulePitchLines = 4;

    /// <summary>The art fills the page; the title sits on a solid band across the top, the author on one across the foot.</summary>
    private static void FullBleedCover(PageDescriptor page, BookletCover cover, byte[] art, Typeset type)
    {
        Setup(page, type);
        page.Margin(0);
        page.Background().Image(art).FitUnproportionally();
        page.Header().Background(Ink).PaddingVertical(28).PaddingHorizontal(56).Column(band =>
        {
            band.Spacing(6);
            band.Item().AlignCenter().Text(text =>
            {
                text.AlignCenter();
                text.ClampLines(ArtTitleLines, "…");
                text.Span(cover.Title).FontSize(30).SemiBold().LineHeight(1.1f).FontColor(Colors.White);
            });
            foreach (var line in new[] { cover.SystemLine, cover.LevelsLine }.OfType<string>())
            {
                band.Item().AlignCenter().Text(line).FontSize(12).FontColor(Colors.White);
            }
        });
        page.Footer().Background(Ink).PaddingVertical(14).AlignCenter().Text(cover.AuthorLine).FontSize(12).FontColor(Colors.White);
    }

    /// <summary>How far the title's scroll sits below the top of the page, and in from its sides.</summary>
    private const float ScrollTop = 40;
    private const float ScrollSide = 44;

    /// <summary>How far the author line's baseline area sits above the foot of the page.</summary>
    private const float AuthorBottom = 34;

    /// <summary>The author line's size, and the room around it its shade (if any) fills.</summary>
    private const float AuthorSize = 13;
    private const float AuthorPadVertical = 8;
    private const float AuthorPadHorizontal = 30;

    /// <summary>
    /// The strip of the page the author line's colour is judged on, as fractions of the page: the
    /// middle of the width (the line is centred and rarely wider) and the height the line and its
    /// padding take above <see cref="AuthorBottom"/>.
    /// </summary>
    internal static readonly (float Left, float Top, float Right, float Bottom) AuthorStrip = (
        0.15f,
        1 - (AuthorBottom + AuthorSize * 1.2f + 2 * AuthorPadVertical) / PageSizes.Letter.Height,
        0.85f,
        1 - AuthorBottom / PageSizes.Letter.Height);

    /// <summary>
    /// The art fills the page; the title, system and levels sit in a gilded frame near the top
    /// (GildedFrameArt, drawn to the type's size), and the author line is set on the art in white or ink,
    /// on a soft shade when the art behind it is too uneven for either (ArtContrast).
    /// </summary>
    private static void ScrollBannerCover(PageDescriptor page, BookletCover cover, byte[] art, Typeset type)
    {
        Setup(page, type);
        page.Margin(0);
        page.Background().Image(art).FitUnproportionally();
        page.Header().PaddingTop(ScrollTop).PaddingHorizontal(ScrollSide).Layers(layers =>
        {
            layers.Layer().Svg(size => GildedFrameArt.Svg(size.Width, size.Height));
            layers.PrimaryLayer()
                .PaddingLeft(GildedFrameArt.SideClearance).PaddingRight(GildedFrameArt.SideClearance + GildedFrameArt.ShadowOffset)
                .PaddingTop(GildedFrameArt.TopClearance).PaddingBottom(GildedFrameArt.BottomClearance + GildedFrameArt.ShadowOffset)
                .Column(scroll =>
            {
                scroll.Spacing(5);
                scroll.Item().AlignCenter().Text(text =>
                {
                    text.AlignCenter();
                    text.ClampLines(ArtTitleLines, "…");
                    text.Span(cover.Title).FontSize(30).SemiBold().LineHeight(1.1f).FontColor(Ink);
                });
                foreach (var line in new[] { cover.SystemLine, cover.LevelsLine }.OfType<string>())
                {
                    scroll.Item().AlignCenter().Text(line).FontSize(12).FontColor(Accent);
                }
            });
        });

        var author = ArtContrast.For(art, AuthorStrip.Left, AuthorStrip.Top, AuthorStrip.Right, AuthorStrip.Bottom);
        page.Footer().PaddingBottom(AuthorBottom).PaddingHorizontal(ScrollSide).AlignCenter().Layers(layers =>
        {
            if (author.Shade is { } shade)
            {
                layers.Layer().Svg(size => ArtContrast.ShadeSvg(size.Width, size.Height, shade));
            }

            layers.PrimaryLayer().PaddingVertical(AuthorPadVertical).PaddingHorizontal(AuthorPadHorizontal).Text(text =>
            {
                text.AlignCenter();
                text.ClampLines(1, "…");
                text.Span(cover.AuthorLine).FontSize(AuthorSize).SemiBold().LetterSpacing(0.02f).FontColor(author.Color);
            });
        });
    }

    /// <summary>The title and its lines above, the art in a ruled frame, the author below.</summary>
    private static void FramedCover(PageDescriptor page, BookletCover cover, byte[] art, Typeset type)
    {
        Setup(page, type);
        page.Content().PaddingTop(12).Column(col =>
        {
            col.Spacing(6);
            col.Item().AlignCenter().Text(text =>
            {
                text.AlignCenter();
                text.ClampLines(2, "…");
                text.Span(cover.Title).FontSize(28).SemiBold().LineHeight(1.1f);
            });
            foreach (var line in new[] { cover.SystemLine, cover.LevelsLine }.OfType<string>())
            {
                col.Item().AlignCenter().Text(line).FontSize(12);
            }

            col.Item().PaddingTop(14).AlignCenter().Width(FrameWidth).Height(FrameHeight)
                .Border(1.5f).BorderColor(Accent).Padding(FramePadding).Image(art).FitUnproportionally();
            col.Item().PaddingTop(12).AlignCenter().Text(cover.AuthorLine).FontSize(12);
        });
    }

    /// <summary>A coloured title band at the top, the art across the middle, the details and pitch below, as the old modules did.</summary>
    private static void ClassicModuleCover(PageDescriptor page, BookletCover cover, byte[] art, Typeset type)
    {
        Setup(page, type);
        page.Content().Column(col =>
        {
            col.Item().Background(Accent).PaddingVertical(22).PaddingHorizontal(28).AlignCenter()
                .Text(text =>
            {
                text.AlignCenter();
                text.ClampLines(2, "…");
                text.Span(cover.Title).FontSize(28).SemiBold().LineHeight(1.1f).FontColor(Colors.White);
            });
            col.Item().PaddingTop(14).Height(ModuleArtHeight).Image(art).FitUnproportionally();
            col.Item().PaddingTop(14).Column(details =>
            {
                details.Spacing(4);
                foreach (var line in new[] { cover.SystemLine, cover.LevelsLine }.OfType<string>())
                {
                    details.Item().AlignCenter().Text(line).FontSize(12).SemiBold();
                }

                details.Item().AlignCenter().Text(cover.AuthorLine).FontSize(12);
                if (cover.Pitch is { } pitch)
                {
                    details.Item().PaddingTop(8).PaddingHorizontal(36).AlignCenter()
                        .Text(text =>
            {
                text.AlignCenter();
                text.ClampLines(ModulePitchLines, "…");
                text.Span(pitch).FontSize(11).Italic();
            });
                }
            });
        });
    }

    /// <summary>The cover with no art (feature 34): type only.</summary>
    private static void TypesetCover(PageDescriptor page, BookletCover cover, Typeset type)
    {
        Setup(page, type);
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
        });
    }

    /// <summary>
    /// The rights page (feature 38): centred in small serif at the foot of the page, as a
    /// published book's is, under the Nornis Press mark, the credits above a hairline and the
    /// notices below it.
    /// </summary>
    private static void Rights(PageDescriptor page, BookletRights rights, Typeset type)
    {
        Setup(page, type);
        page.Content().AlignBottom().PaddingHorizontal(72).Column(col =>
        {
            col.Spacing(3);
            col.Item().PaddingBottom(12).AlignCenter().Width(PressMarkSize).Height(PressMarkSize).Image(PressMark).FitArea();
            col.Item().AlignCenter().Text(rights.Title).FontSize(11).SemiBold();
            col.Item().AlignCenter().Text(rights.Credits.ToUpperInvariant())
                .FontFamily(Sans, Fallback).FontSize(7.5f).LetterSpacing(0.08f).FontColor(Muted);
            col.Item().PaddingTop(6).AlignCenter().Text(rights.Copyright).FontSize(9);
            foreach (var line in new[] { rights.SystemLine, rights.LevelsLine }.OfType<string>())
            {
                col.Item().AlignCenter().Text(line).FontSize(9);
            }

            col.Item().AlignCenter().Text(rights.Printed).FontSize(9);
            col.Item().AlignCenter().Text(rights.MadeWith).FontSize(9);

            col.Item().PaddingVertical(10).PaddingHorizontal(96).LineHorizontal(0.5f).LineColor(Hairline);

            col.Item().AlignCenter().Text(rights.AiStatement).FontSize(8.5f).Italic();
            if (rights.WebResearchStatement is { } web)
            {
                col.Item().PaddingTop(2).AlignCenter().Text(web).FontSize(8.5f).Italic();
            }

            col.Item().PaddingTop(4).AlignCenter().Text(rights.Notice).FontSize(8).FontColor(Muted);
        });
    }

    private static void Chapter(PageDescriptor page, string adventureTitle, BookletChapter chapter, Typeset type)
    {
        Setup(page, type);
        page.DefaultTextStyle(s => s.FontFamily(type.Face, Fallback).FontSize(type.Size).LineHeight(type.LineHeight).FontColor(Ink));

        if (chapter.Title is { } title)
        {
            page.Header().ShowOnce().PaddingBottom(12).Column(header =>
            {
                header.Item().Text(title).FontSize(type.Pt(20)).SemiBold().LineHeight(1.15f);
                header.Item().PaddingTop(4).LineHorizontal(1).LineColor(Accent);
            });
        }

        page.Content().MultiColumn(columns =>
        {
            columns.Columns(2);
            columns.Spacing(Gutter);
            columns.BalanceHeight(false);
            columns.Spacer().AlignCenter().LineVertical(0.5f).LineColor(Hairline);
            columns.Content().Column(col => Blocks(col, chapter.Blocks, BlockSpacing, type, setOff: type.LinePt));
        });

        page.Footer().PaddingTop(8).AlignCenter().Text(t =>
        {
            t.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).FontSize(8).FontColor(Muted));
            t.Span(adventureTitle + " · ");
            t.CurrentPageNumber();
        });
    }

    // ------------------------------------------------------------------- Handouts --

    /// <summary>A run of the appendix: the drawn pages, then the PDF that follows them, if any.</summary>
    private sealed record HandoutRun(IReadOnlyList<BookletHandout> Drawn, BookletHandoutPdf? Pdf);

    /// <summary>
    /// The appendix as runs. The first run's drawn pages render with the booklet; each later
    /// run's render as their own document, merged after the PDF before them. A PDF that won't
    /// open is drawn as a "couldn't print" page instead, so a bad file never fails the booklet.
    /// </summary>
    private static List<HandoutRun> Runs(IReadOnlyList<BookletHandout> handouts)
    {
        var runs = new List<HandoutRun>();
        var drawn = new List<BookletHandout>();
        foreach (var handout in handouts)
        {
            if (handout is BookletHandoutPdf pdf && Opens(pdf.Pdf))
            {
                runs.Add(new HandoutRun(drawn, pdf));
                drawn = [];
            }
            else
            {
                drawn.Add(handout is BookletHandoutPdf broken ? new BookletHandoutMissing(broken.Title, "the PDF couldn't be read") : handout);
            }
        }

        runs.Add(new HandoutRun(drawn, null));
        return runs;
    }

    private static bool Opens(byte[] pdf)
    {
        try
        {
            using var document = PdfDocument.Open(pdf);
            return document.NumberOfPages > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The booklet, then each run's PDF and drawn pages in order, through QuestPDF's merge (the D1
    /// spike: Community licence, files only, so the parts go through a temporary folder).
    /// </summary>
    private static byte[] Merge(byte[] booklet, IReadOnlyList<HandoutRun> runs, Typeset type)
    {
        var folder = Directory.CreateTempSubdirectory("nornis-booklet-");
        try
        {
            var parts = new List<string>();
            string Write(byte[] bytes)
            {
                var path = Path.Combine(folder.FullName, $"{parts.Count}.pdf");
                File.WriteAllBytes(path, bytes);
                parts.Add(path);
                return path;
            }

            var first = Write(booklet);
            for (var i = 0; i < runs.Count; i++)
            {
                if (runs[i].Pdf is { } pdf)
                {
                    Write(pdf.Pdf);
                }

                // The first run's drawn pages are already in the booklet; a later run's follow its PDF.
                var next = i + 1 < runs.Count ? runs[i + 1].Drawn : [];
                if (next.Count > 0)
                {
                    Write(Document.Create(document => DrawnHandouts(document, next, type)).GeneratePdf());
                }
            }

            var output = Path.Combine(folder.FullName, "booklet.pdf");
            var operation = DocumentOperation.LoadFile(first);
            foreach (var part in parts.Skip(1))
            {
                operation = operation.MergeFile(part);
            }

            operation.Save(output);
            return File.ReadAllBytes(output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    /// <summary>The appendix's first page: its title and what follows, in order.</summary>
    private static void HandoutsOpener(PageDescriptor page, IReadOnlyList<BookletHandout> handouts, Typeset type)
    {
        Setup(page, type);
        page.Header().PaddingBottom(12).Column(header =>
        {
            header.Item().Text("Handouts and maps").FontSize(20).SemiBold().LineHeight(1.15f);
            header.Item().PaddingTop(4).LineHorizontal(1).LineColor(Accent);
        });
        page.Content().Column(col =>
        {
            col.Spacing(4);
            for (var i = 0; i < handouts.Count; i++)
            {
                var number = i + 1;
                col.Item().Text(text =>
                {
                    text.Span($"{number}. ").FontColor(Muted);
                    text.Span(handouts[i].Title);
                });
            }
        });
    }

    /// <summary>Images on their own pages, and the pages naming what couldn't be printed.</summary>
    private static void DrawnHandouts(IDocumentContainer document, IReadOnlyList<BookletHandout> handouts, Typeset type)
    {
        foreach (var handout in handouts)
        {
            if (handout is BookletHandoutImage image && ImageSize(image.Image) is { } size)
            {
                document.Page(page => ImageHandout(page, image, landscape: size.Width > size.Height, type));
            }
            else
            {
                var reason = handout is BookletHandoutMissing missing ? missing.Reason : "the image couldn't be read";
                document.Page(page => MissingHandout(page, handout.Title, reason, type));
            }
        }
    }

    /// <summary>The image's size from its header, or null when no codec reads it.</summary>
    private static (int Width, int Height)? ImageSize(byte[] image)
    {
        using var data = SkiaSharp.SKData.CreateCopy(image);
        using var codec = SkiaSharp.SKCodec.Create(data);
        return codec is null ? null : (codec.Info.Width, codec.Info.Height);
    }

    private static void ImageHandout(PageDescriptor page, BookletHandoutImage image, bool landscape, Typeset type)
    {
        Setup(page, type);
        page.Size(landscape ? PageSizes.Letter.Landscape() : PageSizes.Letter);
        page.Header().PaddingBottom(8).Text(image.Title).FontFamily(Sans, Fallback).FontSize(9).FontColor(Muted);
        page.Content().AlignCenter().AlignMiddle().Image(image.Image).FitArea();
    }

    private static void MissingHandout(PageDescriptor page, string title, string reason, Typeset type)
    {
        Setup(page, type);
        page.Content().AlignMiddle().Column(col =>
        {
            col.Spacing(6);
            col.Item().AlignCenter().Text(title).FontSize(16).SemiBold();
            col.Item().AlignCenter().Text($"This handout isn't printed here: {reason}. It's in the world's Library.")
                .FontSize(10).FontColor(Muted);
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
    /// <para>
    /// QuestPDF's <c>EnsureSpace</c> only acts on content that doesn't fit entirely, so it guards
    /// the group (which holds the large block), never a heading by itself.
    /// </para>
    /// <para>
    /// With <paramref name="setOff"/>, a set-off block (see <see cref="IsSetOff"/>) gets that much
    /// more space above and below it than the run's spacing, as does a heading group that
    /// introduces one, so the heading stays tight to its box; so does a lead-in paragraph
    /// (<see cref="IsLeadIn"/>) straight before one. The space is one gap between
    /// neighbours, never doubled between two set-off blocks, and never before the run's first
    /// block or after its last.
    /// </para>
    /// </remarks>
    private static void Blocks(ColumnDescriptor col, IReadOnlyList<BookletBlock> blocks, float spacing, Typeset type, float setOff = 0)
    {
        col.Spacing(spacing);
        bool? previousSetOff = null;
        var previousIntroduces = false;
        for (var i = 0; i < blocks.Count; i++)
        {
            // The block this unit is about: the block itself, or the block a heading group introduces.
            var lead = i;
            while (lead < blocks.Count - 1 && IsHeading(blocks[lead]))
            {
                lead++;
            }

            // A lead-in ("Opening line:") stays with the set-off block after it and takes its space above.
            var introduces = setOff > 0 && lead < blocks.Count - 1 && IsLeadIn(blocks[lead]) && IsSetOff(blocks[lead + 1]);
            var setsOff = setOff > 0 && (IsSetOff(blocks[lead]) || introduces);
            if (previousSetOff is { } before && (before || setsOff) && !previousIntroduces)
            {
                SetOffGap(col, setOff - spacing);
            }

            previousSetOff = setsOff;
            previousIntroduces = introduces;

            if (!IsHeading(blocks[i]))
            {
                Block(col.Item(), blocks[i], type);
                continue;
            }

            var group = new List<BookletBlock>();
            while (i < blocks.Count && IsHeading(blocks[i]))
            {
                group.Add(blocks[i++]);
            }

            var room = group.Count * type.Pt(HeadingRoomPt);
            var keepWhole = true;
            if (i < blocks.Count)
            {
                room += FirstPiecePt(blocks[i], type);
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
                    Block(inner.Item(), member, type);
                }
            });
        }
    }

    /// <summary>
    /// The extra space around a set-off block, as an item of its own between two others (the column
    /// adds its spacing on both sides of it, so it is that much shorter than the gap it makes up).
    /// It takes what is left of a column up to its height and never more, so it never pushes
    /// anything on, and it never opens a column as blank space: at a column's foot it is cut short
    /// and the next block starts the next column at its top.
    /// </summary>
    private static void SetOffGap(ColumnDescriptor col, float height)
    {
        if (height > 0)
        {
            col.Item().MaxHeight(height).ExtendVertical();
        }
    }

    /// <summary>
    /// The blocks the reader's eye stops at, set off from the text around them by a line's space:
    /// every box (read-aloud, prop, ask, GM), stat and item blocks, and code. Tables, lists,
    /// headings and rules sit in the flow.
    /// </summary>
    internal static bool IsSetOff(BookletBlock block) => block is BookletBox or BookletGameBlock or BookletCode;

    /// <summary>A short paragraph ending in a colon ("Opening line:"): it labels the block after it.</summary>
    internal static bool IsLeadIn(BookletBlock block) =>
        block is BookletParagraph paragraph && Chars(paragraph.Text) <= LeadInMaxChars
        && paragraph.Text.LastOrDefault() is BookletRun run && run.Text.TrimEnd().EndsWith(':');

    /// <summary>A lead-in is a label, not prose: a line or so at most.</summary>
    internal const int LeadInMaxChars = 80;

    /// <summary>A heading's height with the spacing after it, at its largest size, for the default body size.</summary>
    internal const float HeadingRoomPt = 27;

    /// <summary>
    /// The height of the first piece of a large block that can't split: what must fit under its
    /// heading for the heading not to be left alone. It is an estimate made before layout.
    /// </summary>
    internal static float FirstPiecePt(BookletBlock block, Typeset type) => block switch
    {
        BookletList { Items.Count: > 0 } list => IsSmallItem(list.Items[0])
            ? Math.Max(1, (Chars(list.Items[0].Blocks) + type.LineChars - 1) / type.LineChars) * type.LinePt
            : 2 * type.LinePt,
        BookletBox => type.Pt(20) + 2 * type.LinePt,
        BookletTable => 3 * type.LinePt,
        BookletGameBlock game => type.Pt((game.Block.Name is null ? 0 : 20) + 14) + 2 * type.LinePt,
        BookletCode => type.Pt(12) + 2 * type.LinePt,
        _ => 2 * type.LinePt,
    };

    private static bool IsSmallItem(BookletListItem item) => Chars(item.Blocks) <= KeepWholeTextChars;

    private static bool IsHeading(BookletBlock block) => block is BookletSectionHeading or BookletHeading;

    /// <summary>Whether a block is small enough to keep whole in one column. It is an estimate made before layout.</summary>
    internal static bool IsSmall(BookletBlock block) => block switch
    {
        BookletBox box => Chars(box.Blocks) <= KeepWholeMaxChars,
        BookletGameBlock game => GameBlockLines(game.Block) <= KeepWholeMaxStatLines,
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
        BookletGameBlock g => g.Block.Lines.Sum(l => (l.Label?.Length ?? 0) + l.Value.Length) + g.Block.Description.Sum(d => d.Length),
        BookletCode c => c.Text.Length,
        _ => 0,
    });

    private static int Chars(IReadOnlyList<BookletInline> text) => text.Sum(i => i is BookletRun r ? r.Text.Length : 1);

    private static void Block(IContainer container, BookletBlock block, Typeset type)
    {
        switch (block)
        {
            case BookletSectionHeading section:
                container.PaddingTop(6).Text(section.Text.ToUpperInvariant())
                    .FontFamily(Sans, Fallback).FontSize(type.Pt(10.5f)).SemiBold().LetterSpacing(0.05f).FontColor(Accent);
                break;
            case BookletHeading heading:
                container.PaddingTop(2).Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).SemiBold().FontSize(type.Pt(heading.Level switch
                    {
                        1 => 13,
                        2 => 12,
                        _ => 10.5f,
                    })));
                    Inlines(t, heading.Text, type);
                });
                break;
            case BookletParagraph paragraph:
                var whole = Chars(paragraph.Text) <= KeepWholeTextChars ? container.PreventPageBreak() : container;
                whole.Text(t => Inlines(t, paragraph.Text, type));
                break;
            case BookletList list:
                List(container, list, type);
                break;
            case BookletBox box:
                Box(IsSmall(box) ? container.PreventPageBreak() : container, box, type);
                break;
            case BookletGameBlock game:
                GameBlock(IsSmall(game) ? container.PreventPageBreak() : container, game.Block, type);
                break;
            case BookletTable table:
                Table(container, table, type);
                break;
            case BookletCode code:
                (IsSmall(code) ? container.PreventPageBreak() : container)
                    .Background(CodeShade).Padding(6)
                    .Text(code.Text).FontFamily(Mono, Fallback).FontSize(type.Pt(8.5f)).LineHeight(1.2f);
                break;
            case BookletRule:
                container.PaddingVertical(4).LineHorizontal(0.5f).LineColor(Hairline);
                break;
        }
    }

    private static void List(IContainer container, BookletList list, Typeset type)
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
                    var markerCell = row.ConstantItem(type.Pt(item.Task != BookletTask.None ? 14 : list.Style == BookletListStyle.Numbered ? 16 : 10));
                    if (item.Task != BookletTask.None)
                    {
                        var box = markerCell.PaddingTop(type.Pt(3)).Width(type.Pt(8)).Height(type.Pt(8)).Border(0.75f).BorderColor(Ink);
                        if (item.Task == BookletTask.Done)
                        {
                            box.AlignCenter().AlignMiddle().Text("✓").FontFamily(Sans, Fallback).FontSize(type.Pt(7)).LineHeight(1);
                        }
                    }
                    else
                    {
                        markerCell.Text(marker);
                    }

                    row.RelativeItem().Column(inner => Blocks(inner, item.Blocks, 2, type));
                });
            }
        });
    }

    private static void Box(IContainer container, BookletBox box, Typeset type)
    {
        var framed = box.Kind switch
        {
            BookletBoxKind.Prop => container.Background(Parchment).Border(0.75f).BorderColor(ParchmentEdge).Padding(9),
            BookletBoxKind.Ask => container.BorderLeft(2).BorderColor(Ink).PaddingLeft(8).PaddingVertical(2),
            // A GM callout is the loudest box and survives grey printing: a heavy ink rule on a shaded ground.
            BookletBoxKind.Gm => container.Background(CodeShade).BorderLeft(4).BorderColor(Ink).PaddingVertical(6).PaddingHorizontal(8),
            _ => container.Border(0.75f).BorderColor(Hairline).BorderLeft(3).BorderColor(Accent).Background(Colors.White)
                .PaddingVertical(7).PaddingHorizontal(9),
        };

        framed.Column(col =>
        {
            if (box.Kind == BookletBoxKind.ReadAloud)
            {
                col.Item().DefaultTextStyle(s => s.Italic()).Column(inner => Blocks(inner, box.Blocks, 4, type));
            }
            else if (box.Kind == BookletBoxKind.Ask && box.Blocks.Count > 0)
            {
                // A question's lead is bold, so it is found at a glance mid-session.
                col.Spacing(4);
                col.Item().DefaultTextStyle(s => s.SemiBold()).Column(inner => Blocks(inner, [box.Blocks[0]], 4, type));
                col.Item().Column(inner => Blocks(inner, box.Blocks.Skip(1).ToList(), 4, type));
            }
            else
            {
                col.Item().Column(inner => Blocks(inner, box.Blocks, 4, type));
            }
        });
    }

    /// <summary>A game block's printed lines, counted for keep-together: name, subtitle, group headings, lines and description.</summary>
    internal static int GameBlockLines(GameBlock block) =>
        (block.Name is null ? 0 : 1) + (block.Subtitle is null ? 0 : 1) + block.Groups.Count(g => g.Heading is not null)
        + block.Lines.Count() + block.Description.Count;

    /// <summary>Runs of at least this many short entries print as a row of cells (STR 14, DEX 12…).</summary>
    internal const int ScoreRunMin = 3;

    /// <summary>A score row holds at most this many cells before it wraps.</summary>
    internal const int ScoreRowMax = 6;

    private const int ScoreLabelMaxChars = 4;
    private const int ScoreValueMaxChars = 8;

    /// <summary>
    /// A stat block (feature 35): an ink frame; the name on an accent rule and the subtitle in
    /// italics; the ungrouped lines as a label column, with runs of short scores as a row of
    /// cells; each group under a small-caps rule, its entries run in ("Cutlass. +5 to hit").
    /// An item is a card with a heavy top rule instead, so it never reads as a prop, and its
    /// description under a hairline. An unnamed, ungrouped block prints as feature 34 printed it.
    /// </summary>
    private static void GameBlock(IContainer container, GameBlock block, Typeset type)
    {
        var body = block.Kind == GameBlockKind.Stat
            ? container.Border(1.5f).BorderColor(Ink).Padding(6)
            : ItemFrame(container);

        body.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).FontSize(type.Pt(8.5f)).LineHeight(1.25f)).Column(col =>
        {
            col.Spacing(3);
            if (block.Name is { } name)
            {
                var title = col.Item().Text(name).FontSize(type.Pt(block.Kind == GameBlockKind.Stat ? 11 : 10.5f)).SemiBold();
                if (block.Kind == GameBlockKind.Stat)
                {
                    title.FontColor(Accent);
                }
            }

            if (block.Subtitle is { } subtitle)
            {
                col.Item().Text(subtitle).FontFamily(type.Face, Fallback).FontSize(type.Pt(9)).Italic();
            }

            if (block.Kind == GameBlockKind.Stat && block.Name is not null)
            {
                col.Item().LineHorizontal(1.5f).LineColor(Accent);
            }

            foreach (var group in block.Groups)
            {
                if (group.Heading is { } heading)
                {
                    // The heading, its rule and its first line move on together, so a group
                    // heading never ends a column of a block long enough to flow.
                    col.Item().PreventPageBreak().Column(head =>
                    {
                        head.Spacing(3);
                        head.Item().PaddingTop(3).Text(heading.ToUpperInvariant())
                            .FontSize(type.Pt(7.5f)).SemiBold().LetterSpacing(0.06f).FontColor(Accent);
                        head.Item().LineHorizontal(0.5f).LineColor(Hairline);
                        if (group.Lines.Count > 0)
                        {
                            RunIn(head.Item(), group.Lines[0]);
                        }
                    });

                    foreach (var line in group.Lines.Skip(1))
                    {
                        RunIn(col.Item(), line);
                    }

                    continue;
                }

                foreach (var run in Runs(group.Lines))
                {
                    if (run.IsScores)
                    {
                        ScoreRows(col.Item(), run.Lines, type);
                    }
                    else
                    {
                        LabelTable(col.Item(), run.Lines, type);
                    }
                }
            }

            if (block.Description.Count > 0)
            {
                col.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Hairline);
                col.Item().Text(string.Join('\n', block.Description)).FontFamily(type.Face, Fallback).FontSize(type.Pt(9.5f)).LineHeight(type.LineHeight);
            }
        });
    }

    /// <summary>A group's entry, run in: "Cutlass. +5 to hit".</summary>
    private static void RunIn(IContainer container, GameBlockLine line) =>
        container.Text(t =>
        {
            if (line.Label is { } label)
            {
                t.Span(label.TrimEnd('.') + ". ").SemiBold().Italic();
            }

            t.Span(line.Value);
        });

    private static IContainer ItemFrame(IContainer container)
    {
        IContainer inner = container;
        container.Column(card =>
        {
            card.Item().LineHorizontal(2).LineColor(Ink);
            inner = card.Item().BorderLeft(0.75f).BorderRight(0.75f).BorderBottom(0.75f).BorderColor(Hairline).Padding(7);
        });
        return inner;
    }

    private static void LabelTable(IContainer container, IReadOnlyList<GameBlockLine> lines, Typeset type) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.ConstantColumn(type.Pt(62));
                c.RelativeColumn();
            });

            foreach (var line in lines)
            {
                if (line.Label is null)
                {
                    table.Cell().ColumnSpan(2).PaddingVertical(1).Text(line.Value);
                    continue;
                }

                table.Cell().PaddingVertical(1).PaddingRight(4).Text(line.Label).SemiBold();
                table.Cell().PaddingVertical(1).Text(line.Value);
            }
        });

    private static void ScoreRows(IContainer container, IReadOnlyList<GameBlockLine> scores, Typeset type)
    {
        var perRow = Math.Min(ScoreRowMax, scores.Count);
        container.PaddingVertical(2).Column(rows =>
        {
            rows.Spacing(3);
            foreach (var chunk in scores.Chunk(perRow))
            {
                rows.Item().Row(row =>
                {
                    for (var i = 0; i < perRow; i++)
                    {
                        var cell = row.RelativeItem();
                        if (i < chunk.Length)
                        {
                            cell.Column(c =>
                            {
                                c.Item().AlignCenter().Text(chunk[i].Label!).SemiBold().FontSize(type.Pt(7.5f));
                                c.Item().AlignCenter().Text(chunk[i].Value);
                            });
                        }
                    }
                });
            }
        });
    }

    internal sealed record LineRun(bool IsScores, IReadOnlyList<GameBlockLine> Lines);

    /// <summary>Splits a group's lines into runs of short scores and runs of everything else, in order.</summary>
    internal static IReadOnlyList<LineRun> Runs(IReadOnlyList<GameBlockLine> lines)
    {
        var runs = new List<LineRun>();
        var i = 0;
        while (i < lines.Count)
        {
            var end = i;
            while (end < lines.Count && IsScore(lines[end]))
            {
                end++;
            }

            if (end - i >= ScoreRunMin)
            {
                runs.Add(new LineRun(true, lines.Skip(i).Take(end - i).ToList()));
                i = end;
                continue;
            }

            var start = i;
            i = Math.Max(end, i + 1);
            while (i < lines.Count && !StartsScoreRun(lines, i))
            {
                i++;
            }

            if (runs.Count > 0 && !runs[^1].IsScores)
            {
                runs[^1] = runs[^1] with { Lines = [.. runs[^1].Lines, .. lines.Skip(start).Take(i - start)] };
            }
            else
            {
                runs.Add(new LineRun(false, lines.Skip(start).Take(i - start).ToList()));
            }
        }

        return runs;
    }

    private static bool StartsScoreRun(IReadOnlyList<GameBlockLine> lines, int i) =>
        i + ScoreRunMin <= lines.Count && lines.Skip(i).Take(ScoreRunMin).All(IsScore);

    private static bool IsScore(GameBlockLine line) =>
        line.Label is { Length: > 0 and <= ScoreLabelMaxChars } && line.Value.Length is > 0 and <= ScoreValueMaxChars;

    private static void Table(IContainer container, BookletTable table, Typeset type)
    {
        var columns = Math.Max(table.Header.Count, table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count));
        if (columns == 0)
        {
            return;
        }

        container.DefaultTextStyle(s => s.FontFamily(Sans, Fallback).FontSize(type.Pt(8.5f)).LineHeight(1.25f)).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                foreach (var width in ColumnWidths(table, columns, type.Pt(1)))
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
                                Inlines(x, text, type);
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
                    t.Cell().ShowEntire().BorderBottom(0.5f).BorderColor(Hairline).Padding(3).Text(x => Inlines(x, text, type));
                }
            }
        });
    }

    /// <summary>A table cell's characters are about this wide at the table's 8.5 pt, at the default body size.</summary>
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
    /// <param name="scale">How much larger than the default the table's type is set.</param>
    internal static ColumnWidth[] ColumnWidths(BookletTable table, int columns, float scale = 1)
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
        var shortWidths = longest.Select(l => l <= ShortColumnChars ? Fit(l, scale) : 0f).ToArray();
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
            var squeezed = shared.Where(i => left * weights[i] / weights.Values.Sum() < Fit(longestWord[i], scale)).ToList();
            if (squeezed.Count == 0 || squeezed.Count == shared.Count)
            {
                return Enumerable.Range(0, columns)
                    .Select(i => widths[i] is { } w ? new ColumnWidth(true, w) : new ColumnWidth(false, weights[i]))
                    .ToArray();
            }

            foreach (var i in squeezed)
            {
                widths[i] = Fit(longestWord[i], scale);
            }
        }

        return widths.Select(w => new ColumnWidth(true, w!.Value)).ToArray();
    }

    private static float Fit(int chars, float scale) => chars * TableCharPt * scale + 8;

    private static int LongestWord(IReadOnlyList<BookletInline> text) =>
        string.Concat(text.OfType<BookletRun>().Select(r => r.Text))
            .Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length).DefaultIfEmpty(0).Max();

    // -------------------------------------------------------------------- Inlines --

    private static void Inlines(TextDescriptor text, IReadOnlyList<BookletInline> inlines, Typeset type)
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
                span.FontFamily(Mono, Fallback).FontSize(type.Pt(8.5f));
            }
        }
    }

    // ----------------------------------------------------------------------- Type --

    /// <summary>
    /// The GM's type, resolved for layout: the body face, size and line height. Every other size
    /// in the body (headings, stat blocks, tables, code) was set for the default 10 pt body and
    /// scales with <see cref="Pt"/>, so the book keeps its proportions at any size.
    /// </summary>
    internal readonly record struct Typeset(string Face, float Size, float LineHeight)
    {
        public const float DefaultSize = (float)BookTypography.DefaultSize;

        public const float DefaultLineHeight = (float)BookTypography.DefaultLineSpacing;

        /// <summary>Body text characters in a column's line at the default size, roughly.</summary>
        private const int DefaultLineChars = 52;

        public static Typeset Of(BookTypography? type)
        {
            type ??= new BookTypography();
            return new Typeset(BookTypography.NameOf(type.FontOrDefault), (float)type.SizeOrDefault, (float)type.LineSpacingOrDefault);
        }

        /// <summary>A size set for the default body, at this body's size.</summary>
        public float Pt(float atDefault) => atDefault * Size / DefaultSize;

        /// <summary>One line of body text.</summary>
        public float LinePt => Size * LineHeight;

        /// <summary>Body text characters in a column's line, roughly.</summary>
        public int LineChars => Math.Max(1, (int)MathF.Round(DefaultLineChars * DefaultSize / Size));
    }
}
