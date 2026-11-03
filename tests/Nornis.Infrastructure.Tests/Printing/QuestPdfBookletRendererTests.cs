using System.Diagnostics;
using Nornis.Application.Configuration;
using Nornis.Application.Prep;
using Nornis.Domain.Enums;
using Nornis.Infrastructure.Printing;
using NUnit.Framework;
using QuestPDF.Fluent;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Nornis.Infrastructure.Tests.Printing;

/// <summary>
/// The booklet's layout rules (feature 34), checked on the PDF itself: PdfPig reads every word's
/// page and position back out, so "a heading never ends a column" is asserted where the reader
/// would see it, not on QuestPDF's fluent calls. Each fixture is built from the booklet records
/// directly, so a failure here is a layout failure and never a Markdown one.
/// </summary>
[TestFixture]
public class QuestPdfBookletRendererTests
{
    // US Letter, 0.6in margins, 0.3in gutter: the two columns' edges in points.
    private const double PageWidth = 612;
    private const double PageHeight = 792;
    private const double ContentLeft = 43.2;
    private const double ContentRight = PageWidth - 43.2;
    private const double Middle = PageWidth / 2;
    private const double HalfGutter = 10.8;

    /// <summary>Anything this low on a page is the footer, not the flow.</summary>
    private const double FooterTop = 64;

    /// <summary>The cover is page 1 and the rights page page 2 (feature 38); the first chapter starts here.</summary>
    private const int FirstChapterPage = 3;

    private static readonly BookletCover Cover = new(
        "The Silver Key", "Shadowdark", "Levels 1–3", "by Dave", "A heist under the harbour.");

    private static readonly BookletRights Rights = new(
        "The Silver Key", "Nornis Press", "Nornis adventure · Nornis Press · 2026", "© 2026 Dave", "Shadowdark", "Levels 1–3",
        "Printed 3 November 2026", "Made with Nornis 3.9", "Text written with AI assistance.", "rightsnotice is a trademark line.");

    private readonly QuestPdfBookletRenderer _renderer = new();

    // ------------------------------------------------------------------- Pages --

    [Test]
    public void TheCover_IsPageOneAlone()
    {
        var pdf = Render(Chapter("Arrival", P("chapterword one.")));

        using var document = PdfDocument.Open(pdf.Bytes);
        var cover = Words(document.GetPage(1)).Select(w => w.Text).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(pdf.Pages, Is.EqualTo(3));
            Assert.That(cover, Does.Contain("Silver").And.Contain("Shadowdark").And.Contain("heist"));
            Assert.That(cover, Does.Not.Contain("chapterword"), "nothing of the adventure shares the cover's page");
        });
    }

    /// <summary>Feature 38, Requirement 2: the rights page follows the cover, alone, its notices below the credits.</summary>
    [Test]
    public void TheRightsPage_IsPageTwoAlone_CreditsAboveTheNotices()
    {
        var pdf = Render(Chapter("Arrival", P("chapterword one.")));

        using var document = PdfDocument.Open(pdf.Bytes);
        var cover = Words(document.GetPage(1)).Select(w => w.Text).ToList();
        var words = Words(document.GetPage(2)).ToList();
        var text = words.Select(w => w.Text).ToList();
        double TopOf(string token) => words.First(w => w.Text == token).BoundingBox.Top;

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("©").And.Contain("Printed").And.Contain("Made").And.Contain("assistance.")
                .And.Contain("rightsnotice"));
            Assert.That(text, Does.Not.Contain("chapterword"));
            Assert.That(cover, Does.Not.Contain("PRESS"), "the credits moved off the cover");
            Assert.That(TopOf("PRESS"), Is.GreaterThan(TopOf("assistance.")), "the credits sit above the AI statement");
            Assert.That(TopOf("assistance."), Is.GreaterThan(TopOf("rightsnotice")), "and the notice last");
            Assert.That(TopOf("rightsnotice"), Is.LessThan(PageHeight / 2), "at the foot of the page, as a book's is");
        });
    }

    // ------------------------------------------------------------- Cover art --

    /// <summary>Portrait art the size the image model makes, in two colours so a crop shows.</summary>
    private static byte[] Art(int width = 1024, int height = 1536)
    {
        using var bitmap = new SkiaSharp.SKBitmap(width, height);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.DarkGreen);
            using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Goldenrod };
            canvas.DrawCircle(width / 2f, height / 2f, width / 3f, paint);
        }

        using var image = SkiaSharp.SKImage.FromBitmap(bitmap);
        return image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100).ToArray();
    }

    private BookletPdf RenderWithArt(CoverLayout layout, byte[]? art = null, string? title = null, string? pitch = null) =>
        _renderer.Render(new Booklet(
            Cover with { Title = title ?? Cover.Title, Pitch = pitch ?? Cover.Pitch, Art = new BookletArt(art ?? Art(), layout) },
            Rights, [Chapter("Arrival", P("chapterword one."))]));

    /// <summary>Feature 38, Requirement 3.6: each layout is one page, carries the art and its type, and the rights page stays page two.</summary>
    [TestCase(CoverLayout.FullBleed)]
    [TestCase(CoverLayout.Framed)]
    [TestCase(CoverLayout.ClassicModule)]
    public void EachLayout_IsOnePage_WithTheArtAndTheType(CoverLayout layout)
    {
        var pdf = RenderWithArt(layout);

        using var document = PdfDocument.Open(pdf.Bytes);
        var cover = document.GetPage(1);
        var words = Words(cover).Select(w => w.Text).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(pdf.Pages, Is.EqualTo(3));
            Assert.That(cover.GetImages().Count(), Is.EqualTo(1));
            Assert.That(words, Does.Contain("Silver").And.Contain("Shadowdark").And.Contain("Dave"));
            Assert.That(Words(document.GetPage(2)).Select(w => w.Text), Does.Contain("©"), "the rights page is still page two");
        });
    }

    [Test]
    public void FullBleedArt_FillsThePage_AndTheTitleSitsOnItsBand()
    {
        var pdf = RenderWithArt(CoverLayout.FullBleed);

        using var document = PdfDocument.Open(pdf.Bytes);
        var image = document.GetPage(1).GetImages().Single().BoundingBox;
        var title = Words(document.GetPage(1)).First(w => w.Text == "Silver").BoundingBox;
        Assert.Multiple(() =>
        {
            Assert.That(image.Width, Is.EqualTo(PageWidth).Within(1));
            Assert.That(image.Height, Is.EqualTo(PageHeight).Within(1));
            Assert.That(title.Bottom, Is.GreaterThan(PageHeight * 0.8), "the title is in the band across the top");
        });
    }

    [Test]
    public void FramedArt_SitsBelowTheTitle_AndInsideTheMargins()
    {
        var pdf = RenderWithArt(CoverLayout.Framed);

        using var document = PdfDocument.Open(pdf.Bytes);
        var image = document.GetPage(1).GetImages().Single().BoundingBox;
        var title = Words(document.GetPage(1)).First(w => w.Text == "Silver").BoundingBox;
        Assert.Multiple(() =>
        {
            Assert.That(image.Left, Is.GreaterThan(ContentLeft));
            Assert.That(image.Right, Is.LessThan(ContentRight));
            Assert.That(image.Top, Is.LessThan(title.Bottom), "type sits off the art");
        });
    }

    [Test]
    public void TheClassicModule_PutsTheDetailsAndPitchBelowTheArt()
    {
        var pdf = RenderWithArt(CoverLayout.ClassicModule);

        using var document = PdfDocument.Open(pdf.Bytes);
        var image = document.GetPage(1).GetImages().Single().BoundingBox;
        var words = Words(document.GetPage(1)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(words.First(w => w.Text == "Silver").BoundingBox.Bottom, Is.GreaterThan(image.Top));
            Assert.That(words.First(w => w.Text == "heist").BoundingBox.Top, Is.LessThan(image.Bottom));
        });
    }

    /// <summary>A title or pitch as long as the bounds allow still leaves the cover one page.</summary>
    [TestCase(CoverLayout.FullBleed)]
    [TestCase(CoverLayout.Framed)]
    [TestCase(CoverLayout.ClassicModule)]
    public void TheLongestTitleAndPitch_NeverPushTheCoverOntoASecondPage(CoverLayout layout)
    {
        var title = string.Join(' ', Enumerable.Repeat("Interminable", 16));
        var pitch = string.Join(' ', Enumerable.Repeat("The baths steam and the slimes wait for bathers.", 80));

        var pdf = RenderWithArt(layout, title: title, pitch: pitch);

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.That(pdf.Pages, Is.EqualTo(3));
        Assert.That(Words(document.GetPage(2)).Select(w => w.Text), Does.Contain("©"));
    }

    [Test]
    public void ArtThatCannotBeDecoded_PrintsTheTypesetCover()
    {
        var pdf = RenderWithArt(CoverLayout.FullBleed, art: [1, 2, 3, 4]);

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.That(document.GetPage(1).GetImages(), Is.Empty);
        Assert.That(Words(document.GetPage(1)).Select(w => w.Text), Does.Contain("heist"));
    }

    [Test]
    public void TheArt_IsKeptAtPrintResolution_NotLarger()
    {
        var huge = RenderWithArt(CoverLayout.FullBleed, art: Art(4000, 6000));

        using var document = PdfDocument.Open(huge.Bytes);
        var image = document.GetPage(1).GetImages().Single();
        Assert.That(image.WidthInSamples, Is.LessThanOrEqualTo(PageWidth / 72 * CoverArtImage.PrintDpi + 1));
    }

    // --------------------------------------------------------------- Handouts --

    /// <summary>A real one-page PDF whose only word is <paramref name="word"/>, as a GM's handout would be.</summary>
    private static byte[] OnePagePdf(string word) =>
        Document.Create(d => d.Page(p => p.Content().Text(word))).GeneratePdf();

    private BookletPdf RenderWithHandouts(params BookletHandout[] handouts) =>
        _renderer.Render(new Booklet(Cover, Rights, [Chapter("Arrival", P("chapterword one."))], handouts));

    /// <summary>Feature 38, Requirement 4.4: the appendix follows the chapters, in the GM's order, PDFs merged where they stand.</summary>
    [Test]
    public void TheAppendix_KeepsTheGmsOrder_AcrossImagesAndMergedPdfs()
    {
        var pdf = RenderWithHandouts(
            new BookletHandoutImage("Harbor map", Art(1536, 1024)),
            new BookletHandoutPdf("Forged letter", OnePagePdf("letterword")),
            new BookletHandoutImage("Bathhouse sketch", Art()),
            new BookletHandoutPdf("Wanted poster", OnePagePdf("posterword")));

        using var document = PdfDocument.Open(pdf.Bytes);
        string PageText(int n) => string.Join(' ', Words(document.GetPage(n)).Select(w => w.Text));
        Assert.Multiple(() =>
        {
            // Cover, rights, one chapter, the opener, then the four handouts.
            Assert.That(pdf.Pages, Is.EqualTo(8));
            Assert.That(PageText(4), Does.Contain("Handouts and maps").And.Contain("Harbor").And.Contain("Wanted"));
            Assert.That(PageText(5), Does.Contain("Harbor map"));
            Assert.That(PageText(6), Does.Contain("letterword"));
            Assert.That(PageText(7), Does.Contain("Bathhouse sketch"));
            Assert.That(PageText(8), Does.Contain("posterword"));
            Assert.That(PageText(1), Does.Contain("Silver"), "the merge keeps the booklet itself first");
        });
    }

    [Test]
    public void AWideImage_GetsALandscapePage_AndATallOneAPortraitPage()
    {
        var pdf = RenderWithHandouts(new BookletHandoutImage("Wide", Art(1536, 1024)), new BookletHandoutImage("Tall", Art()));

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.Multiple(() =>
        {
            Assert.That(document.GetPage(5).Width, Is.EqualTo(PageHeight).Within(1));
            Assert.That(document.GetPage(6).Width, Is.EqualTo(PageWidth).Within(1));
            Assert.That(document.GetPage(5).GetImages().Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void AHandoutThatCantBePrinted_IsAPageThatSaysSo_AndTheBookletStillPrints()
    {
        var pdf = RenderWithHandouts(
            new BookletHandoutPdf("Broken letter", [1, 2, 3]),
            new BookletHandoutImage("Broken map", [4, 5, 6]),
            new BookletHandoutMissing("Lost sketch", "its file is missing"));

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.Multiple(() =>
        {
            Assert.That(pdf.Pages, Is.EqualTo(7));
            Assert.That(Words(document.GetPage(5)).Select(w => w.Text), Does.Contain("Broken").And.Contain("Library."));
            Assert.That(Words(document.GetPage(7)).Select(w => w.Text), Does.Contain("missing."));
        });
    }

    [Test]
    public void NoHandouts_NoAppendix()
    {
        var pdf = Render(Chapter("Arrival", P("chapterword one.")));

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.That(AllWords(document).Select(w => w.Word.Text), Does.Not.Contain("Handouts"));
    }

    [Test]
    public void TheProps_AreOnePerPage_InLargeType()
    {
        var props = new BookletProps("The Silver Key",
        [
            new BookletBox(BookletBoxKind.Prop, [P("letterword from the mayor.")]),
            new BookletBox(BookletBoxKind.Prop, [P("posterword: wanted.")]),
        ]);

        var pdf = _renderer.RenderProps(props);

        using var document = PdfDocument.Open(pdf.Bytes);
        var letter = Words(document.GetPage(1)).First(w => w.Text == "letterword");
        Assert.Multiple(() =>
        {
            Assert.That(pdf.Pages, Is.EqualTo(2));
            Assert.That(Words(document.GetPage(2)).Select(w => w.Text), Does.Contain("posterword:"));
            Assert.That(letter.BoundingBox.Height, Is.GreaterThan(10), "large type, to read across a table");
        });
    }

    [Test]
    public void EachChapter_StartsAPage_EvenWhenThePageBeforeHasRoom()
    {
        var pdf = Render(
            Chapter("Arrival", P("alphaword is short.")),
            Chapter("Departure", P("betaword is short too.")));

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.Multiple(() =>
        {
            Assert.That(pdf.Pages, Is.EqualTo(4));
            Assert.That(Words(document.GetPage(FirstChapterPage)).Select(w => w.Text), Does.Contain("Arrival").And.Contain("alphaword"));
            Assert.That(Words(document.GetPage(FirstChapterPage + 1)).Select(w => w.Text), Does.Contain("Departure").And.Contain("betaword")
                .And.Not.Contain("alphaword"));
        });
    }

    // -------------------------------------------------------------------- Flow --

    [Test]
    public void TheText_FlowsDownTheLeftColumn_ThenTheRight_ThenTheNextPage()
    {
        var tokens = Enumerable.Range(1, 160).Select(i => $"flow{i:D3}").ToList();
        var pdf = Render(Chapter("Arrival", tokens.Select((t, i) => P($"{t} {Sentence(1 + i % 2)}")).ToArray()));

        var found = Locate(pdf, tokens);

        Assert.Multiple(() =>
        {
            Assert.That(found.Select(f => f.Page).Distinct().Count(), Is.GreaterThan(1), "the fixture must span pages");
            Assert.That(found.Select(f => f.Column).Distinct().Count(), Is.EqualTo(2), "and both columns");
            Assert.That(found.OrderBy(f => f.Page).ThenBy(f => f.Column).ThenByDescending(f => f.Top).Select(f => f.Token),
                Is.EqualTo(tokens), "a reader going down each column in turn meets the text in the order it was written");
        });
    }

    [Test]
    public void AHeading_NeverEndsAColumn()
    {
        // Sections of uneven length and every kind of block, some small enough to keep whole and
        // some not, so headings fall at every height of a column with every kind of block under them.
        var blocks = new List<BookletBlock>();
        for (var i = 0; i < 90; i++)
        {
            blocks.Add(new BookletSectionHeading($"headmark{i:D2}", AdventureSectionKind.Scene));
            if (i % 3 == 0)
            {
                blocks.Add(new BookletHeading(2, Inl($"subhead{i:D2}")));
            }

            var size = 1 + i * 7 % 9;
            blocks.Add((i % 5) switch
            {
                0 => new BookletList(BookletListStyle.Bullet, 1,
                    [.. Enumerable.Range(0, size).Select(n => new BookletListItem(BookletTask.None, [P($"body{i:D2} {Sentence(1 + n % 4)}")]))]),
                1 => new BookletBox(BookletBoxKind.ReadAloud, [.. Enumerable.Range(0, size).Select(_ => P($"body{i:D2} {Sentence(2)}"))]),
                2 => new BookletTable([Inl("When"), Inl("What")],
                    [.. Enumerable.Range(0, size * 2).Select(n => (IReadOnlyList<IReadOnlyList<BookletInline>>)[Inl($"0:{n:D2}"), Inl($"body{i:D2} {Sentence(1)}")])]),
                _ => P($"body{i:D2} {Sentence(size)}"),
            });
        }

        var pdf = Render(Chapter("Arrival", [.. blocks]));

        using var document = PdfDocument.Open(pdf.Bytes);
        var lastLines = new List<string>();
        for (var page = FirstChapterPage; page <= document.NumberOfPages; page++)
        {
            foreach (var column in Words(document.GetPage(page)).Where(w => w.BoundingBox.Bottom > FooterTop).GroupBy(ColumnOf))
            {
                var lowest = column.Min(w => w.BoundingBox.Bottom);
                lastLines.AddRange(column.Where(w => w.BoundingBox.Bottom - lowest < 2).Select(w => w.Text));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(document.NumberOfPages, Is.GreaterThan(2), "the fixture must cross several column feet");
            Assert.That(lastLines.Where(t => t.StartsWith("HEADMARK", StringComparison.Ordinal) || t.StartsWith("subhead", StringComparison.Ordinal)), Is.Empty);
        });
    }

    [Test]
    public void ASmallBox_NearAColumnsFoot_MovesOnWhole()
    {
        var box = Enumerable.Range(1, 6).Select(i => $"boxline{i}").ToList();
        var splitWouldHappen = 0;

        // The box lands at a different height for each count of filler, so one of them puts it
        // across a column's foot.
        for (var filler = 8; filler < 22; filler++)
        {
            var blocks = Enumerable.Range(0, filler).Select(i => P($"fill{i} {Sentence(2)}")).ToList<BookletBlock>();
            blocks.Add(new BookletBox(BookletBoxKind.ReadAloud, [.. box.Select(t => P($"{t} the vault door is cold."))]));
            blocks.Add(P("after the box."));

            var found = Locate(Render(Chapter("Arrival", [.. blocks])), [$"fill{filler - 1}", .. box]);
            var places = found.Skip(1).Select(f => (f.Page, f.Column)).Distinct().ToList();
            Assert.That(places, Has.Count.EqualTo(1), $"with {filler} filler paragraphs the box split across {string.Join(", ", places)}");

            if ((found[0].Page, found[0].Column) != places[0])
            {
                splitWouldHappen++;
            }
        }

        Assert.That(splitWouldHappen, Is.GreaterThan(0), "no filler count put the box at a column's foot, so the test proves nothing");
    }

    [Test]
    public void ALargeBox_StartsWhereItFalls_AndFlowsOn()
    {
        var lines = Enumerable.Range(1, 60).Select(i => $"longbox{i:D2}").ToList();
        var pdf = Render(Chapter("Arrival",
            P($"opening {Sentence(3)}"),
            new BookletBox(BookletBoxKind.Prop, [.. lines.Select(t => P($"{t} {Sentence(1)}"))])));

        var found = Locate(pdf, lines);

        Assert.Multiple(() =>
        {
            Assert.That((found[0].Page, found[0].Column), Is.EqualTo((FirstChapterPage, 0)), "the box starts under the opening paragraph");
            Assert.That(found.Select(f => (f.Page, f.Column)).Distinct().Count(), Is.GreaterThan(1), "and flows into the next column");
        });
    }

    // ------------------------------------------------------------------ Tables --

    [Test]
    public void ALongTable_RepeatsItsHeader_InEveryColumnItReaches()
    {
        var rows = Enumerable.Range(1, 150)
            .Select(i => (IReadOnlyList<IReadOnlyList<BookletInline>>)[Inl($"0:{i:D2}"), Inl($"row{i:D3} the guards change shift")])
            .ToList();
        var pdf = Render(Chapter("Arrival", new BookletTable([Inl("hdrwhen"), Inl("hdrwhat")], rows)));

        var rowPlaces = Locate(pdf, rows.Select((_, i) => $"row{i + 1:D3}").ToList()).Select(f => (f.Page, f.Column)).Distinct().ToList();
        using var document = PdfDocument.Open(pdf.Bytes);
        var headerPlaces = AllWords(document).Where(w => w.Word.Text == "hdrwhen").Select(w => (w.Page, ColumnOf(w.Word))).Distinct().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(rowPlaces, Has.Count.GreaterThan(1), "the fixture must span columns");
            Assert.That(headerPlaces, Is.EquivalentTo(rowPlaces));
        });
    }

    [Test]
    public void AShortColumn_IsSizedToFit_AndTheRestShare()
    {
        var table = new BookletTable([Inl("When"), Inl("What happens")],
        [
            [Inl("0:00-0:20"), Inl("The guards change shift and the lamplighter makes his round of the harbour.")],
            [Inl("0:20-0:40"), Inl("The vault's clockwork lock resets; anyone inside is trapped until dawn.")],
        ]);

        var widths = QuestPdfBookletRenderer.ColumnWidths(table, 2);

        Assert.Multiple(() =>
        {
            Assert.That(widths[0].Fixed, Is.True);
            Assert.That(widths[0].Value, Is.GreaterThanOrEqualTo("0:00-0:20".Length * 4.7f), "the time fits on one line");
            Assert.That(widths[1].Fixed, Is.False);
        });
    }

    [Test]
    public void ALongWord_IsNeverNarrowerThanItsColumn()
    {
        var longText = "The guards change shift and the lamplighter makes his round of the harbour, slowly.";
        var table = new BookletTable([Inl("Checkpoint"), Inl("Detail"), Inl("Notes")],
        [
            [Inl("Counterweighted portcullis"), Inl(longText + " " + longText), Inl(longText + " " + longText)],
        ]);

        var widths = QuestPdfBookletRenderer.ColumnWidths(table, 3);

        Assert.Multiple(() =>
        {
            Assert.That(widths[0].Fixed, Is.True, "a squeezed column is fixed at its longest word");
            Assert.That(widths[0].Value, Is.GreaterThanOrEqualTo("Counterweighted".Length * 4.7f));
            Assert.That(widths.Skip(1).All(w => !w.Fixed), Is.True);
        });
    }

    [Test]
    public void ATableOfOnlyLongColumns_SharesTheWidth()
    {
        var text = Inl("The guards change shift and the lamplighter makes his round.");
        var widths = QuestPdfBookletRenderer.ColumnWidths(new BookletTable([Inl("A"), Inl("B")], [[text, text]]), 2);

        Assert.That(widths.Select(w => w.Fixed), Is.All.False);
    }

    // ------------------------------------------------------------------- Lists --

    [Test]
    public void EveryLetter_OfAList_StaysInsideItsColumn()
    {
        var items = Enumerable.Range(1, 40)
            .Select(i => new BookletListItem(i % 5 == 0 ? BookletTask.Done : BookletTask.None, [P($"item{i:D2} {Sentence(2)}")]))
            .ToList();
        var pdf = Render(Chapter("Arrival",
            new BookletList(BookletListStyle.Bullet, 1, items),
            new BookletList(BookletListStyle.Numbered, 7, items.Take(12).ToList())));

        using var document = PdfDocument.Open(pdf.Bytes);
        var outside = new List<string>();
        var bullets = 0;
        for (var page = FirstChapterPage; page <= document.NumberOfPages; page++)
        {
            foreach (var letter in document.GetPage(page).Letters.Where(l => l.BoundingBox.Bottom > FooterTop && !string.IsNullOrWhiteSpace(l.Value)))
            {
                bullets += letter.Value == "•" ? 1 : 0;
                var box = letter.BoundingBox;
                var inLeft = box.Left >= ContentLeft - 0.5 && box.Right <= Middle - HalfGutter + 0.5;
                var inRight = box.Left >= Middle + HalfGutter - 0.5 && box.Right <= ContentRight + 0.5;
                if (!inLeft && !inRight)
                {
                    outside.Add($"'{letter.Value}' on page {page} at x {box.Left:F1}–{box.Right:F1}");
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(document.NumberOfPages, Is.GreaterThan(2), "the list must cross into the right column and on");
            Assert.That(bullets, Is.EqualTo(32), "every undone item has its bullet");
            Assert.That(outside, Is.Empty);
        });
    }

    // ------------------------------------------------------------- Game blocks --

    private static BookletGameBlock Game(GameBlockKind kind, string text) => new(GameBlocks.Parse(kind, text.Split('\n')));

    [Test]
    public void AStatBlock_PrintsItsNameAboveItsSubtitle_AboveItsEntries()
    {
        var pdf = Render(Chapter("Arrival", Game(GameBlockKind.Stat, "# Vossname\n*Subtitleword human*\nArmour: 15 entryword")));

        var found = Locate(pdf, ["Vossname", "Subtitleword", "entryword"]);

        Assert.That(found.Select(f => f.Top), Is.Ordered.Descending, "name, then subtitle, then the entries, top to bottom");
    }

    [Test]
    public void ShortScores_ShareOneRow_AndWrapAfterSix()
    {
        var labels = new[] { "STR", "DEX", "CON", "INT", "WIS", "CHA", "LCK", "SAN" };
        var pdf = Render(Chapter("Arrival", Game(GameBlockKind.Stat,
            "# Voss\n" + string.Join('\n', labels.Select((l, i) => $"{l}: {10 + i}")))));

        var found = Locate(pdf, labels);
        var firstRow = found.Take(6).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(firstRow.Select(f => Math.Round(f.Top)).Distinct().Count(), Is.EqualTo(1), "six labels on one baseline");
            Assert.That(firstRow.Select(f => f.Left), Is.Ordered.Ascending, "left to right in the order written");
            Assert.That(found[6].Top, Is.LessThan(firstRow[0].Top - 5), "the seventh wraps to a second row");
            Assert.That(Math.Abs(found[6].Left - firstRow[0].Left), Is.LessThan(1), "under the first, in the same cell width");
        });
    }

    [Test]
    public void AnItemCard_NearAColumnsFoot_MovesOnWhole()
    {
        var lines = Enumerable.Range(1, 5).Select(i => $"cardline{i}").ToList();
        var item = Game(GameBlockKind.Item,
            "# Cloak\n*Wondrous item*\n" + string.Join('\n', lines.Take(3).Select(t => $"{t}: 1 lb")) + "\n---\n"
            + string.Join('\n', lines.Skip(3).Select(t => $"{t} keeps the wearer dry.")));
        var splitWouldHappen = 0;

        for (var filler = 8; filler < 22; filler++)
        {
            var blocks = Enumerable.Range(0, filler).Select(i => P($"fill{i} {Sentence(2)}")).ToList<BookletBlock>();
            blocks.Add(item);
            blocks.Add(P("after the card."));

            var found = Locate(Render(Chapter("Arrival", [.. blocks])), [$"fill{filler - 1}", .. lines]);
            var places = found.Skip(1).Select(f => (f.Page, f.Column)).Distinct().ToList();
            Assert.That(places, Has.Count.EqualTo(1), $"with {filler} filler paragraphs the card split across {string.Join(", ", places)}");
            if ((found[0].Page, found[0].Column) != places[0])
            {
                splitWouldHappen++;
            }
        }

        Assert.That(splitWouldHappen, Is.GreaterThan(0), "no filler count put the card at a column's foot, so the test proves nothing");
    }

    [Test]
    public void AGroupHeading_NeverEndsAColumn_OfALongStatBlock()
    {
        var text = "# Hydra\n" + string.Join('\n', Enumerable.Range(0, 24).Select(g =>
            $"## grpmark{g:D2}\n" + string.Join('\n', Enumerable.Range(0, 1 + g % 3).Select(n => $"Bite {n}: {Sentence(1)}"))));
        var lastLines = new List<string>();
        var pages = 0;

        // The opening's length moves every group heading down a little each time, so across the
        // runs some heading meets a column's foot.
        for (var opening = 0; opening < 16; opening++)
        {
            var pdf = Render(Chapter("Arrival", P("opening " + string.Join(' ', Enumerable.Repeat("word", opening * 9))),
                Game(GameBlockKind.Stat, text)));
            using var document = PdfDocument.Open(pdf.Bytes);
            pages = Math.Max(pages, document.NumberOfPages);
            for (var page = FirstChapterPage; page <= document.NumberOfPages; page++)
            {
                foreach (var column in Words(document.GetPage(page)).Where(w => w.BoundingBox.Bottom > FooterTop).GroupBy(ColumnOf))
                {
                    var lowest = column.Min(w => w.BoundingBox.Bottom);
                    lastLines.AddRange(column.Where(w => w.BoundingBox.Bottom - lowest < 2).Select(w => $"{opening}:{w.Text}"));
                }
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(pages, Is.GreaterThan(2), "the block must flow across column feet");
            Assert.That(lastLines.Where(t => t.Contains(":GRPMARK", StringComparison.Ordinal)), Is.Empty);
        });
    }

    [Test]
    public void Runs_SplitShortScoresFromEverythingElse_InOrder()
    {
        GameBlockLine[] lines =
        [
            new("AC", "15"), new("HP", "27 (5d8+5)"),
            new("STR", "14"), new("DEX", "16"), new("CON", "12"),
            new("Senses", "darkvision 60 ft."), new("WIS", "10"), new("CHA", "8"),
        ];

        var runs = QuestPdfBookletRenderer.Runs(lines);

        Assert.That(runs.Select(r => (r.IsScores, r.Lines.Count)), Is.EqualTo(((bool, int)[])
        [
            (false, 2),
            (true, 3),
            (false, 3),
        ]), "two scores are not a row; 'HP 27 (5d8+5)' is too long to be one");
    }

    // ------------------------------------------------------------------ Bounds --

    [Test]
    public void AnEmoji_RendersWithoutThrowing()
    {
        var pdf = Render(Chapter("Arrival 🗝️", P("The dragon 🐉 sleeps; the key ✨ glows.")));

        Assert.That(Locate(pdf, ["dragon"]), Has.Count.EqualTo(1));
    }

    [Test]
    public void TheLargestAdventurePrepAllows_RendersUnderTheLibrarysLimit()
    {
        // PrepOptions' bounds: 200 sections, 97,500 characters in all, in every kind of block.
        var prep = new PrepOptions();
        var perSection = prep.MaxAdventureChars / prep.MaxSections;
        var chapters = new List<BookletChapter>();
        for (var c = 0; c < 20; c++)
        {
            var blocks = new List<BookletBlock>();
            for (var s = 0; s < prep.MaxSections / 20; s++)
            {
                blocks.Add(new BookletSectionHeading($"Scene {c}.{s}", AdventureSectionKind.Scene));
                blocks.Add((s % 4) switch
                {
                    0 => new BookletBox(BookletBoxKind.ReadAloud, [P(Text(perSection))]),
                    1 => new BookletTable([Inl("When"), Inl("What")], [[Inl("0:00"), Inl(Text(perSection - 4))]]),
                    2 => new BookletGameBlock(new GameBlock(GameBlockKind.Stat, "Guard", null, [new GameBlockGroup(null, [new GameBlockLine("AC", "15"), new GameBlockLine(null, Text(perSection - 4))])], [])),
                    _ => new BookletList(BookletListStyle.Bullet, 1, [new BookletListItem(BookletTask.None, [P(Text(perSection))])]),
                });
            }

            chapters.Add(new BookletChapter($"Chapter {c}", blocks));
        }

        var clock = Stopwatch.StartNew();
        var pdf = _renderer.Render(new Booklet(Cover, Rights, chapters));
        clock.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(pdf.Bytes.Length, Is.LessThan(new LibraryOptions().MaxUploadSizeBytes));
            Assert.That(pdf.Bytes.Length, Is.LessThan(20 * 1024 * 1024), "far under it, in practice");
            Assert.That(pdf.Pages, Is.InRange(22, 200));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "rendering is on the request thread");
        });
    }

    [Test]
    public void ThePageCount_IsThePdfsOwn()
    {
        var pdf = Render(Chapter("Arrival", P("one.")), Chapter(null, P("two.")));

        using var document = PdfDocument.Open(pdf.Bytes);
        Assert.That(pdf.Pages, Is.EqualTo(document.NumberOfPages));
    }

    // ----------------------------------------------------------------- helpers --

    private sealed record Found(string Token, int Page, int Column, double Top, double Left);

    private BookletPdf Render(params BookletChapter[] chapters) => _renderer.Render(new Booklet(Cover, Rights, chapters));

    private static BookletChapter Chapter(string? title, params BookletBlock[] blocks) => new(title, blocks);

    private static BookletParagraph P(string text) => new(Inl(text));

    private static IReadOnlyList<BookletInline> Inl(string text) => [new BookletRun(text)];

    private static string Sentence(int count) => string.Join(' ', Enumerable.Repeat(
        "The lamplighter crosses the harbour bridge as the bells ring and the tide turns against the quay.", count));

    private static string Text(int chars)
    {
        var text = Sentence(chars / 90 + 1);
        return text[..Math.Min(chars, text.Length)];
    }

    private static IEnumerable<Word> Words(Page page) => page.GetWords();

    private static IEnumerable<(int Page, Word Word)> AllWords(PdfDocument document) =>
        Enumerable.Range(1, document.NumberOfPages).SelectMany(p => Words(document.GetPage(p)).Select(w => (p, w)));

    private static int ColumnOf(Word word) => word.BoundingBox.Left < Middle ? 0 : 1;

    /// <summary>Where each token is, in the order given; every token must appear exactly once.</summary>
    private static List<Found> Locate(BookletPdf pdf, IReadOnlyList<string> tokens)
    {
        using var document = PdfDocument.Open(pdf.Bytes);
        var words = AllWords(document).Where(w => w.Page >= FirstChapterPage).ToList();
        return tokens.Select(token =>
        {
            var hits = words.Where(w => w.Word.Text == token).ToList();
            Assert.That(hits, Has.Count.EqualTo(1), $"'{token}' should appear once");
            return new Found(token, hits[0].Page, ColumnOf(hits[0].Word), hits[0].Word.BoundingBox.Top, hits[0].Word.BoundingBox.Left);
        }).ToList();
    }
}
