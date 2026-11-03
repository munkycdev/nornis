using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// The booklet's structure (feature 34, carrying feature 29's conventions and feature 32's cover):
/// each markup convention becomes its block, markers never print, nothing is passed through as
/// markup, every newline is a line break, and the synthetic fixture builds end to end.
/// </summary>
[TestFixture]
public class BookletModelTests
{
    private static T Single<T>(string markdown) where T : BookletBlock
    {
        var blocks = BookletModel.Blocks(markdown);
        Assert.That(blocks, Has.Count.EqualTo(1), string.Join(", ", blocks));
        return (T)blocks[0];
    }

    private static string Text(IReadOnlyList<BookletInline> inlines) =>
        string.Concat(inlines.Select(i => i is BookletRun r ? r.Text : "\n"));

    private static string Text(IEnumerable<BookletBlock> blocks) => string.Join("\n\n", blocks.Select(b => b switch
    {
        BookletParagraph p => Text(p.Text),
        BookletHeading h => Text(h.Text),
        _ => b.ToString(),
    }));

    // --------------------------------------------------------------------- Boxes --

    [Test]
    public void APlainQuote_IsReadAloud()
    {
        var box = Single<BookletBox>("> Fog sits on the water like wet wool.");

        Assert.That(box.Kind, Is.EqualTo(BookletBoxKind.ReadAloud));
        Assert.That(Text(box.Blocks), Is.EqualTo("Fog sits on the water like wet wool."));
    }

    [Test]
    public void APropQuote_IsAnInWorldDocument_AndTheMarkerDoesNotPrint()
    {
        var box = Single<BookletBox>("> [!PROP]\n> A tide table, folded small.\n> *bring the key*");

        Assert.Multiple(() =>
        {
            Assert.That(box.Kind, Is.EqualTo(BookletBoxKind.Prop));
            Assert.That(Text(box.Blocks), Is.EqualTo("A tide table, folded small.\nbring the key"));
            Assert.That(((BookletParagraph)box.Blocks[0]).Text, Has.Some.EqualTo(new BookletRun("bring the key", BookletRunStyle.Italic)));
        });
    }

    [Test]
    public void AnAskQuote_IsAQuestion_AndTheMarkerDoesNotPrint()
    {
        var box = Single<BookletBox>("> [!ASK]\n> Who on your crew has been here before?");

        Assert.That(box.Kind, Is.EqualTo(BookletBoxKind.Ask));
        Assert.That(Text(box.Blocks), Is.EqualTo("Who on your crew has been here before?"));
    }

    [Test]
    public void AGmQuote_IsTheGmsCallout_AndTheMarkerDoesNotPrint()
    {
        var box = Single<BookletBox>("> [!GM]\n> **Hard exit.** If the floor gives, the slimes win.");

        Assert.That(box.Kind, Is.EqualTo(BookletBoxKind.Gm));
        Assert.That(Text(box.Blocks), Does.StartWith("Hard exit.").And.Not.Contain("[!GM]"));
    }

    [Test]
    public void AMarkerAloneInItsParagraph_IsRemovedWithIt()
    {
        var box = Single<BookletBox>("> [!PROP]\n>\n> A letter, sealed in green wax.");

        Assert.That(box.Kind, Is.EqualTo(BookletBoxKind.Prop));
        Assert.That(Text(box.Blocks), Is.EqualTo("A letter, sealed in green wax."));
    }

    [Test]
    public void AMarkerLaterInAQuote_IsJustText()
    {
        var box = Single<BookletBox>("> The sign reads [!PROP] in red.");

        Assert.That(box.Kind, Is.EqualTo(BookletBoxKind.ReadAloud));
        Assert.That(Text(box.Blocks), Does.Contain("[!PROP]"));
    }

    // --------------------------------------------------------- Stat blocks, code --

    [Test]
    public void AStatBlock_IsAGameBlock_ReadByTheGrammar()
    {
        var stat = Single<BookletGameBlock>("```statblock\nName: Captain Voss\nHP: 27\nSpeed: 30 ft., swim 20 ft.\nKnows every tide\n```");

        Assert.Multiple(() =>
        {
            Assert.That(stat.Block.Kind, Is.EqualTo(GameBlockKind.Stat));
            Assert.That(stat.Block.Name, Is.EqualTo("Captain Voss"));
            Assert.That(stat.Block.Lines, Is.EqualTo((GameBlockLine[])
            [
                new("HP", "27"),
                new("Speed", "30 ft., swim 20 ft."),
                new(null, "Knows every tide"),
            ]));
        });
    }

    [Test]
    public void AnItemBlock_IsAGameBlock_WithItsDescription()
    {
        var item = Single<BookletGameBlock>("```item\n# Rope\n*Adventuring gear*\nLength: 50 ft\n---\nKnotted every foot.\n```");

        Assert.Multiple(() =>
        {
            Assert.That(item.Block.Kind, Is.EqualTo(GameBlockKind.Item));
            Assert.That(item.Block.Subtitle, Is.EqualTo("Adventuring gear"));
            Assert.That(item.Block.Description, Is.EqualTo((string[])["Knotted every foot."]));
        });
    }

    [Test]
    public void AnyOtherFence_IsCodeAsUsual() =>
        Assert.That(Single<BookletCode>("```\nplain\ntext\n```").Text, Is.EqualTo("plain\ntext"));

    // ---------------------------------------------------------- Tables and lists --

    [Test]
    public void APipeTable_IsAHeaderAndRows()
    {
        var table = Single<BookletTable>("| Beat | Clock |\n| --- | --- |\n| Arrival | 0:00 |\n| **Climax** | 1:45 |");

        Assert.Multiple(() =>
        {
            Assert.That(table.Header.Select(Text), Is.EqualTo((string[])["Beat", "Clock"]));
            Assert.That(table.Rows.Select(r => r.Select(Text)), Is.EqualTo([new[] { "Arrival", "0:00" }, ["Climax", "1:45"]]));
            Assert.That(table.Rows[1][0], Is.EqualTo(new BookletInline[] { new BookletRun("Climax", BookletRunStyle.Bold) }));
        });
    }

    [Test]
    public void BulletsAndNumbers_KeepTheirStyleAndStart()
    {
        var bullets = Single<BookletList>("- one\n- two");
        var numbers = Single<BookletList>("3. three\n4. four");

        Assert.Multiple(() =>
        {
            Assert.That(bullets.Style, Is.EqualTo(BookletListStyle.Bullet));
            Assert.That(bullets.Items.Select(i => Text(i.Blocks)), Is.EqualTo((string[])["one", "two"]));
            Assert.That(numbers.Style, Is.EqualTo(BookletListStyle.Numbered));
            Assert.That(numbers.Start, Is.EqualTo(3));
        });
    }

    /// <summary>Feature 34, Requirement 3.2: a checklist prints boxes, not brackets.</summary>
    [Test]
    public void ATaskItem_IsABox_OpenOrDone_AndTheBracketsDoNotPrint()
    {
        var list = Single<BookletList>("- [ ] Accepted the case.\n- [x] Boarded Bickle.\n- Plain item");

        Assert.Multiple(() =>
        {
            Assert.That(list.Items.Select(i => i.Task), Is.EqualTo([BookletTask.Open, BookletTask.Done, BookletTask.None]));
            Assert.That(list.Items.Select(i => Text(i.Blocks)), Is.EqualTo((string[])["Accepted the case.", "Boarded Bickle.", "Plain item"]));
        });
    }

    [Test]
    public void ANestedList_SitsInsideItsItem()
    {
        var list = Single<BookletList>("- outer\n  - inner");

        Assert.That(list.Items.Single().Blocks[1], Is.TypeOf<BookletList>());
    }

    // ------------------------------------------------------------------- Inlines --

    /// <summary>
    /// Feature 34, Requirement 3.1: the editor shows every newline as a line break (the Web's
    /// MarkdownRenderer), so the booklet does too. Before, "20-25 minutes" and "Starting location:"
    /// ran together on one line.
    /// </summary>
    [Test]
    public void EveryNewline_IsALineBreak()
    {
        var paragraph = Single<BookletParagraph>("**Recommended runtime:** 20-25 minutes\n**Starting location:** The Agency");

        Assert.That(paragraph.Text, Is.EqualTo(new BookletInline[]
        {
            new BookletRun("Recommended runtime:", BookletRunStyle.Bold),
            new BookletRun(" 20-25 minutes"),
            new BookletBreak(),
            new BookletRun("Starting location:", BookletRunStyle.Bold),
            new BookletRun(" The Agency"),
        }));
    }

    [Test]
    public void Emphasis_StrikeAndCode_KeepTheirStyles()
    {
        var paragraph = Single<BookletParagraph>("*a* **b** ***c*** ~~d~~ `e`");

        Assert.That(paragraph.Text.OfType<BookletRun>().Where(r => r.Style != BookletRunStyle.None), Is.EqualTo(
        [
            new BookletRun("a", BookletRunStyle.Italic),
            new BookletRun("b", BookletRunStyle.Bold),
            new BookletRun("c", BookletRunStyle.Bold | BookletRunStyle.Italic),
            new BookletRun("d", BookletRunStyle.Strike),
            new BookletRun("e", BookletRunStyle.Code),
        ]));
    }

    /// <summary>Feature 34, Requirement 3.4: nothing in a section makes the API fetch a URL.</summary>
    [Test]
    public void AnImage_PrintsItsAltText_AndALink_ItsText()
    {
        var paragraph = Single<BookletParagraph>("See ![the harbor map](https://example.com/map.png) and [the docks](https://example.com).");

        Assert.Multiple(() =>
        {
            Assert.That(Text(paragraph.Text), Is.EqualTo("See the harbor map and the docks."));
            Assert.That(Text(paragraph.Text), Does.Not.Contain("example.com"));
        });
    }

    [Test]
    public void RawHtml_IsText_NeverMarkup()
    {
        var blocks = BookletModel.Blocks("<script>alert(1)</script>\n\n> <img src=x onerror=alert(1)>");

        Assert.Multiple(() =>
        {
            Assert.That(Text(((BookletParagraph)blocks[0]).Text), Is.EqualTo("<script>alert(1)</script>"));
            Assert.That(Text(((BookletBox)blocks[1]).Blocks), Is.EqualTo("<img src=x onerror=alert(1)>"));
        });
    }

    [Test]
    public void HeadingsInABody_KeepTheirLevel()
    {
        var blocks = BookletModel.Blocks("## Before Play\n\nText\n\n#### Deep");

        Assert.That(blocks.OfType<BookletHeading>().Select(h => (h.Level, Text(h.Text))),
            Is.EqualTo([(2, "Before Play"), (4, "Deep")]));
    }

    [Test]
    public void NoText_IsNoBlocks()
    {
        Assert.That(BookletModel.Blocks(null), Is.Empty);
        Assert.That(BookletModel.Blocks("  \n "), Is.Empty);
    }

    // ------------------------------------------------------------------ Chapters --

    [Test]
    public void ChaptersStartChapters_UnwrittenSectionsAreLeftOut_AndOrderIsSortOrder()
    {
        var adventure = Adventure("The Silver Key",
            ("Scene 1", AdventureSectionKind.Scene, "> The docks."),
            ("Chapter 1", AdventureSectionKind.Chapter, ""),
            ("Scene 2", AdventureSectionKind.Scene, AdventureSkeleton.SceneTemplate),
            ("Quick reference", AdventureSectionKind.QuickReference, ""),
            ("Overview", AdventureSectionKind.Overview, "Before the first chapter."));
        // Put the Chapter first by order, not by list position, and the Overview before it.
        adventure.Sections.First(s => s.Title == "Overview").SortOrder = 0;
        adventure.Sections.First(s => s.Title == "Chapter 1").SortOrder = 1;
        adventure.Sections.First(s => s.Title == "Scene 1").SortOrder = 2;

        var booklet = BookletModel.Build(adventure, Bare, Imprint);

        Assert.Multiple(() =>
        {
            Assert.That(booklet.Chapters.Select(c => c.Title), Is.EqualTo([null, "Chapter 1"]));
            Assert.That(booklet.Chapters[0].Blocks[0], Is.EqualTo(new BookletSectionHeading("Overview", AdventureSectionKind.Overview)));
            Assert.That(booklet.Chapters[1].Blocks[0], Is.EqualTo(new BookletSectionHeading("Scene 1", AdventureSectionKind.Scene)));
            Assert.That(booklet.Chapters[1].Blocks[1], Is.TypeOf<BookletBox>());
            var headings = booklet.Chapters.SelectMany(c => c.Blocks).OfType<BookletSectionHeading>().Select(h => h.Text);
            Assert.That(headings, Has.None.EqualTo("Scene 2"), "an untouched Scene template is unwritten");
            Assert.That(headings, Has.None.EqualTo("Quick reference"), "an empty section is unwritten");
        });
    }

    [Test]
    public void AnAdventureWithNoSections_IsACoverAlone() =>
        Assert.That(BookletModel.Build(Adventure("The Silver Key"), Bare, Imprint).Chapters, Is.Empty);

    [Test]
    public void AnEmptyChapter_StillPrintsItsTitle() =>
        Assert.That(BookletModel.Build(Adventure("The Silver Key", ("Chapter 1", AdventureSectionKind.Chapter, "")), Bare, Imprint).Chapters,
            Is.EqualTo([new BookletChapter("Chapter 1", [])]).Using<BookletChapter>((a, b) => a.Title == b.Title && a.Blocks.Count == b.Blocks.Count));

    [Test]
    public void TheSyntheticFixture_ImportsAndBuildsEveryConvention()
    {
        var text = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Prep", "two-chapters.md"));
        var (imported, error) = AdventureMarkdownImporter.Import([new MarkdownFile("two-chapters.md", text)], 2026);
        Assert.That(error, Is.Null);
        var adventure = Adventure(imported!.Title,
            imported.Sections.Select(s => (s.Title, s.Kind, s.BodyMarkdown)).ToArray());

        var booklet = BookletModel.Build(adventure, Bare, Imprint);
        var blocks = booklet.Chapters.SelectMany(c => c.Blocks).SelectMany(Flatten).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(booklet.Chapters.Select(c => c.Title), Is.EqualTo((string[])["Chapter 1: The Harbor at Dusk", "Chapter 2: Voss's Ship"]));
            Assert.That(blocks.OfType<BookletBox>().Select(b => b.Kind).Distinct(),
                Is.EquivalentTo((BookletBoxKind[])[BookletBoxKind.ReadAloud, BookletBoxKind.Prop, BookletBoxKind.Ask]));
            Assert.That(blocks.OfType<BookletGameBlock>().SelectMany(s => s.Block.Lines), Has.Some.EqualTo(new GameBlockLine("HP", "27")));
            Assert.That(blocks.OfType<BookletTable>(), Is.Not.Empty);
            Assert.That(blocks.OfType<BookletParagraph>().Select(p => Text(p.Text)), Has.None.Contains("[!"));
        });
    }

    private static IEnumerable<BookletBlock> Flatten(BookletBlock block) => block switch
    {
        BookletBox box => box.Blocks.SelectMany(Flatten).Prepend(box),
        BookletList list => list.Items.SelectMany(i => i.Blocks).SelectMany(Flatten).Prepend(list),
        _ => [block],
    };

    // ------------------------------------------------------------ The cover (feature 32) --

    private static readonly PrintDetails Bare = PrintDetails.Resolve("The Silver Key", new AdventureDetails(), null, "kelda", AiUseKind.None);

    private static readonly BookletImprint Imprint = new(new DateOnly(2026, 11, 3), "3.9");

    [Test]
    public void TheCover_HasEachDetail()
    {
        var details = PrintDetails.Resolve("The Silver Key",
            new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st", Levels = "1–3", Year = 2026 }, null, "kelda", AiUseKind.None);

        var cover = BookletModel.Build(Adventure("The Silver Key"), details, Imprint).Cover;

        Assert.That(cover, Is.EqualTo(new BookletCover(
            "The Silver Key", "Shadowdark 1st", "Levels 1–3", "By kelda", "A heist.")));
    }

    [Test]
    public void AnUnsetDetail_IsLeftOffTheCover()
    {
        var cover = BookletModel.Build(Adventure("The Silver Key"), Bare, Imprint).Cover;

        Assert.That(cover, Is.EqualTo(new BookletCover(
            "The Silver Key", null, null, "By kelda", "A heist.")));
    }

    // ------------------------------------------------------- rights page (feature 38) --

    [Test]
    public void TheRightsPage_CarriesTheCredits_TheDate_TheReleaseAndTheNotices()
    {
        var details = PrintDetails.Resolve("The Silver Key",
            new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st", Levels = "1–3", Year = 2025 }, null, "kelda", AiUseKind.Text);

        var rights = BookletModel.Build(Adventure("The Silver Key"), details, Imprint).Rights;

        Assert.That(rights, Is.EqualTo(new BookletRights(
            "The Silver Key", "Nornis Press", "Nornis adventure · Nornis Press · 2025", "© 2025 kelda", "Shadowdark 1st", "Levels 1–3",
            "Printed 3 November 2026", "Made with Nornis 3.9", "Text written with AI assistance.", RightsStatement.Notice)));
    }

    [Test]
    public void WithNoYear_TheCopyrightIsThePrintsYear_AndUnsetLinesAreLeftOff()
    {
        var rights = BookletModel.Build(Adventure("The Silver Key"), Bare, Imprint).Rights;

        Assert.Multiple(() =>
        {
            Assert.That(rights.Copyright, Is.EqualTo("© 2026 kelda"));
            Assert.That(rights.Credits, Is.EqualTo("Nornis adventure · Nornis Press"));
            Assert.That((rights.SystemLine, rights.LevelsLine), Is.EqualTo(((string?)null, (string?)null)));
            Assert.That(rights.AiStatement, Is.EqualTo("No AI was used to write this adventure's text or art."));
        });
    }

    /// <summary>Requirement 2.3: the statement says exactly what was recorded, and each kind has its own.</summary>
    [TestCase(AiUseKind.None, "No AI was used to write this adventure's text or art.")]
    [TestCase(AiUseKind.Declared, "AI was used in making this adventure.")]
    [TestCase(AiUseKind.Text, "Text written with AI assistance.")]
    [TestCase(AiUseKind.Art, "Cover art generated with AI.")]
    [TestCase(AiUseKind.TextAndArt, "Text written and cover art generated with AI assistance.")]
    public void TheAiStatement_SaysWhatWasRecorded(AiUseKind kind, string sentence)
    {
        Assert.That(RightsStatement.For(kind), Is.EqualTo(sentence));
    }

    [Test]
    public void EveryAiUse_HasASentence_AndNoTwoShareOne()
    {
        var sentences = Enum.GetValues<AiUseKind>().Select(RightsStatement.For).ToList();

        Assert.That(sentences, Is.Unique);
    }

    [Test]
    public void TheRelease_IsMajorDotMinor_FromTheBuild()
    {
        Assert.That(RightsStatement.CurrentVersion, Does.Match(@"^\d+\.\d+$"));
    }

    [Test]
    public void AnEditionWithNoGameSystem_StillPrints()
    {
        var details = PrintDetails.Resolve("The Silver Key", new AdventureDetails { Edition = "2nd" }, null, "kelda", AiUseKind.None);

        Assert.That(BookletModel.Build(Adventure("The Silver Key"), details, Imprint).Cover.SystemLine, Is.EqualTo("2nd"));
    }

    private static Adventure Adventure(string title, params (string Title, AdventureSectionKind Kind, string Body)[] sections)
    {
        var id = Guid.NewGuid();
        return new Adventure
        {
            Id = id,
            Title = title,
            Pitch = "A heist.",
            Sections = sections.Select((s, i) => new AdventureSection
            {
                Id = Guid.NewGuid(),
                AdventureId = id,
                Title = s.Title,
                Kind = s.Kind,
                BodyMarkdown = s.Body,
                SortOrder = i + 1,
            }).ToList(),
        };
    }
}
