using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// The booklet's HTML (feature 29, Requirement 7): each markup convention becomes the element the
/// print stylesheet styles, markers never print, raw HTML is escaped, and the synthetic fixture
/// renders through the importer end to end.
/// </summary>
[TestFixture]
public class AdventurePrintModelTests
{
    [Test]
    public void APlainQuote_IsReadAloud()
    {
        var html = AdventurePrintModel.RenderBody("> Fog sits on the water like wet wool.");

        Assert.That(html, Does.Contain("<blockquote class=\"read-aloud\">"));
        Assert.That(html, Does.Contain("Fog sits on the water"));
    }

    [Test]
    public void APropQuote_IsAnInWorldDocument_AndTheMarkerDoesNotPrint()
    {
        var html = AdventurePrintModel.RenderBody("> [!PROP]\n> A tide table, folded small.\n> *bring the key*");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<aside class=\"prop\">"));
            Assert.That(html, Does.Contain("A tide table, folded small."));
            Assert.That(html, Does.Contain("<em>bring the key</em>"));
            Assert.That(html, Does.Not.Contain("[!PROP]"));
            Assert.That(html, Does.Not.Contain("read-aloud"));
        });
    }

    [Test]
    public void AnAskQuote_IsAQuestion_AndTheMarkerDoesNotPrint()
    {
        var html = AdventurePrintModel.RenderBody("> [!ASK]\n> Who on your crew has been here before?");

        Assert.That(html, Does.Contain("<aside class=\"ask\">"));
        Assert.That(html, Does.Contain("Who on your crew"));
        Assert.That(html, Does.Not.Contain("[!ASK]"));
    }

    [Test]
    public void AMarkerAloneInItsParagraph_IsRemovedWithIt()
    {
        var html = AdventurePrintModel.RenderBody("> [!PROP]\n>\n> A letter, sealed in green wax.");

        Assert.That(html, Does.Contain("<aside class=\"prop\">"));
        Assert.That(html, Does.Contain("A letter, sealed in green wax."));
        Assert.That(html, Does.Not.Contain("[!PROP]"));
    }

    [Test]
    public void AMarkerLaterInAQuote_IsJustText()
    {
        var html = AdventurePrintModel.RenderBody("> The sign reads [!PROP] in red.");

        Assert.That(html, Does.Contain("read-aloud"));
        Assert.That(html, Does.Contain("[!PROP]"));
    }

    [Test]
    public void AStatBlock_IsAGridOfLabelsAndValues()
    {
        var html = AdventurePrintModel.RenderBody("```statblock\nName: Captain Voss\nHP: 27\nSpeed: 30 ft., swim 20 ft.\nKnows every tide\n```");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<dl class=\"statblock\">"));
            Assert.That(html, Does.Contain("<dt>Name</dt><dd>Captain Voss</dd>"));
            Assert.That(html, Does.Contain("<dt>Speed</dt><dd>30 ft., swim 20 ft.</dd>"));
            Assert.That(html, Does.Contain("<dd class=\"statblock-wide\">Knows every tide</dd>"));
            Assert.That(html, Does.Not.Contain("<pre>"));
        });
    }

    [Test]
    public void AnyOtherFence_IsCodeAsUsual()
    {
        var html = AdventurePrintModel.RenderBody("```\nplain\n```");

        Assert.That(html, Does.Contain("<pre><code>plain"));
    }

    [Test]
    public void APipeTable_IsATable()
    {
        var html = AdventurePrintModel.RenderBody("| Beat | Clock |\n| --- | --- |\n| Arrival | 0:00 |");

        Assert.That(html, Does.Contain("<table>"));
        Assert.That(html, Does.Contain("<td>Arrival</td>"));
    }

    [Test]
    public void RawHtml_IsEscaped_NeverPassedThrough()
    {
        var html = AdventurePrintModel.RenderBody("<script>alert(1)</script>\n\n> <img src=x onerror=alert(1)>");
        var whole = AdventurePrintModel.Render(Adventure("<b>Title</b>", ("Notes", AdventureSectionKind.Notes, "x")), Bare);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Not.Contain("<img"));
            Assert.That(whole, Does.Not.Contain("<b>Title</b>"));
            Assert.That(whole, Does.Contain("&lt;b&gt;Title&lt;/b&gt;"));
        });
    }

    [Test]
    public void Render_ChaptersOpenPages_UnwrittenSectionsAreLeftOut_AndOrderIsSortOrder()
    {
        var adventure = Adventure("The Silver Key",
            ("Scene 1", AdventureSectionKind.Scene, "> The docks."),
            ("Chapter 1", AdventureSectionKind.Chapter, ""),
            ("Scene 2", AdventureSectionKind.Scene, AdventureSkeleton.SceneTemplate),
            ("Quick reference", AdventureSectionKind.QuickReference, ""));
        // Put the Chapter first by order, not by list position.
        adventure.Sections.First(s => s.Title == "Scene 1").SortOrder = 2;
        adventure.Sections.First(s => s.Title == "Chapter 1").SortOrder = 1;

        var html = AdventurePrintModel.Render(adventure, Bare);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<h1 class=\"booklet-title\">The Silver Key</h1>"));
            Assert.That(html, Does.Contain("<p class=\"booklet-pitch\">A heist.</p>"));
            Assert.That(html, Does.Contain("<section class=\"booklet-chapter booklet-chapter\"><h1 class=\"booklet-chapter-title\">Chapter 1</h1>"));
            Assert.That(html.IndexOf("Chapter 1", StringComparison.Ordinal), Is.LessThan(html.IndexOf("Scene 1", StringComparison.Ordinal)));
            Assert.That(html, Does.Not.Contain("Scene 2"), "an untouched Scene template is unwritten");
            Assert.That(html, Does.Not.Contain("Quick reference"), "an empty section is unwritten");
        });
    }

    [Test]
    public void TheSyntheticFixture_ImportsAndRendersEveryConvention()
    {
        var text = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Prep", "two-chapters.md"));
        var (imported, error) = AdventureMarkdownImporter.Import([new MarkdownFile("two-chapters.md", text)], 2026);
        Assert.That(error, Is.Null);
        var adventure = Adventure(imported!.Title,
            imported.Sections.Select(s => (s.Title, s.Kind, s.BodyMarkdown)).ToArray());

        var html = AdventurePrintModel.Render(adventure, Bare);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("class=\"booklet-chapter-title\">Chapter 1: The Harbor at Dusk</h1>"));
            Assert.That(html, Does.Contain("class=\"booklet-chapter-title\">Chapter 2: Voss&#39;s Ship</h1>"));
            Assert.That(html, Does.Contain("<blockquote class=\"read-aloud\">"));
            Assert.That(html, Does.Contain("<aside class=\"prop\">"));
            Assert.That(html, Does.Contain("<aside class=\"ask\">"));
            Assert.That(html, Does.Contain("<dt>HP</dt><dd>27</dd>"));
            Assert.That(html, Does.Contain("<table>"));
            Assert.That(html, Does.Not.Contain("[!"));
        });
    }

    // ------------------------------------------------------------ The cover (feature 32) --

    private static readonly PrintDetails Bare = PrintDetails.Resolve("The Silver Key", new AdventureDetails(), null, "kelda");

    [Test]
    public void TheCover_PrintsEachDetail_UnderTheTitle_AndTheCreditsAtItsFoot()
    {
        var details = PrintDetails.Resolve("The Silver Key",
            new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st", Levels = "1–3", Year = 2026 }, null, "kelda");

        var html = AdventurePrintModel.Render(Adventure("The Silver Key"), details);
        var cover = html[..(html.IndexOf("</header>", StringComparison.Ordinal))];

        Assert.Multiple(() =>
        {
            Assert.That(cover, Does.Contain("<p class=\"booklet-system\">Shadowdark 1st</p>"));
            Assert.That(cover, Does.Contain("<p class=\"booklet-levels\">Levels 1–3</p>"));
            Assert.That(cover, Does.Contain("<p class=\"booklet-author\">By kelda</p>"));
            Assert.That(cover, Does.Contain("<p class=\"booklet-credits\">Nornis adventure · Nornis Press · 2026</p>"));
            Assert.That(cover.IndexOf("booklet-title", StringComparison.Ordinal),
                Is.LessThan(cover.IndexOf("booklet-system", StringComparison.Ordinal)));
            Assert.That(cover.IndexOf("booklet-pitch", StringComparison.Ordinal),
                Is.LessThan(cover.IndexOf("booklet-credits", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void AnUnsetDetail_IsLeftOffTheCover()
    {
        var html = AdventurePrintModel.Render(Adventure("The Silver Key"), Bare);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("booklet-system"));
            Assert.That(html, Does.Not.Contain("booklet-levels"));
            Assert.That(html, Does.Contain("<p class=\"booklet-author\">By kelda</p>"));
            Assert.That(html, Does.Contain("<p class=\"booklet-credits\">Nornis adventure · Nornis Press</p>"));
        });
    }

    [Test]
    public void TheCoversDetails_AreEncoded()
    {
        var details = PrintDetails.Resolve("The Silver Key",
            new AdventureDetails { GameSystem = "<b>Fate</b>", Levels = "1 & 2" }, null, "<i>kelda</i>");

        var html = AdventurePrintModel.Render(Adventure("The Silver Key"), details);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<b>Fate</b>").And.Not.Contain("<i>kelda</i>"));
            Assert.That(html, Does.Contain("&lt;b&gt;Fate&lt;/b&gt;"));
            Assert.That(html, Does.Contain("Levels 1 &amp; 2"));
        });
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
