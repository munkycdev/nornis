using Nornis.Application.Prep;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

[TestFixture]
public class AdventureMarkdownImporterTests
{
    private static string Fixture() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Prep", "two-chapters.md"));

    private static ImportedAdventure Import(params MarkdownFile[] files)
    {
        var (adventure, error) = AdventureMarkdownImporter.Import(files, ThisYear);
        Assert.That(error, Is.Null);
        return adventure!;
    }

    [Test]
    public void TheFixture_ImportsIntoTheExpectedKindsAndTitles()
    {
        var adventure = Import(new MarkdownFile("two-chapters.md", Fixture()));

        Assert.That(adventure.Title, Is.EqualTo("The Silver Key of Black Harbor"));
        Assert.That(adventure.Sections.Select(s => (s.Kind, s.Title)), Is.EqualTo(
        [
            (AdventureSectionKind.Chapter, "Chapter 1: The Harbor at Dusk"),
            (AdventureSectionKind.Scene, "Scene 1: The Customs House"),
            (AdventureSectionKind.Chapter, "Chapter 2: Voss's Ship"),
            (AdventureSectionKind.Scene, "Scene 2: The Mudlark"),
            (AdventureSectionKind.Notes, "Captain Voss"),
        ]));
    }

    [Test]
    public void EveryLineOfTheFixture_LandsInExactlyOneBody_ApartFromTheHeadingsMovedIntoTitles()
    {
        var source = Fixture().Replace("\r\n", "\n");
        var adventure = Import(new MarkdownFile("two-chapters.md", source));

        // Each body is a verbatim slice of the file: the read-aloud, the prop, the question,
        // the table and the stat block arrive byte for byte.
        foreach (var section in adventure.Sections.Where(s => s.BodyMarkdown.Length > 0))
        {
            Assert.That(source, Does.Contain(section.BodyMarkdown), section.Title);
        }

        var bodyLines = adventure.Sections.SelectMany(s => s.BodyMarkdown.Split('\n')).Where(l => l.Length > 0).ToList();
        var expected = source.Split('\n')
            .Where(l => l.Length > 0 && !l.StartsWith("# ") && !l.StartsWith("## Chapter"))
            .ToList();
        Assert.That(bodyLines, Is.EqualTo(expected));
    }

    [Test]
    public void TheMarkersSurvive()
    {
        var bodies = string.Join("\n", Import(new MarkdownFile("f.md", Fixture())).Sections.Select(s => s.BodyMarkdown));

        Assert.Multiple(() =>
        {
            Assert.That(bodies, Does.Contain($"> {PrepMarkup.PropMarker}\n> A tide table"));
            Assert.That(bodies, Does.Contain($"> {PrepMarkup.AskMarker}\n> Who on your crew"));
            Assert.That(bodies, Does.Contain($"```{PrepMarkup.StatBlockInfo}\nName: Captain Voss"));
            Assert.That(bodies, Does.Contain("| Beat | Clock |\n| --- | --- |"));
        });
    }

    [Test]
    public void ARepeatedTitle_StartsAChapter_InEveryFile_AndOnlyTheFirstFileNamesTheAdventure()
    {
        var adventure = Import(
            new MarkdownFile("one.md", "# The Missing Caravan\n## The Road\n\nDust.\n\n# Scene 1: Tracks\n\nRuts."),
            new MarkdownFile("two.md", "#   the  missing CARAVAN \n## The Camp\n\nAshes.\n\n# Aftermath\n\nQuiet."));

        Assert.That(adventure.Title, Is.EqualTo("The Missing Caravan"));
        Assert.That(adventure.Sections.Select(s => (s.Kind, s.Title, s.BodyMarkdown)), Is.EqualTo(
        [
            (AdventureSectionKind.Chapter, "The Road", "Dust."),
            (AdventureSectionKind.Scene, "Scene 1: Tracks", "Ruts."),
            (AdventureSectionKind.Chapter, "The Camp", "Ashes."),
            (AdventureSectionKind.Notes, "Aftermath", "Quiet."),
        ]));
    }

    [Test]
    public void AHashInsideACodeFence_DoesNotSplit()
    {
        var adventure = Import(new MarkdownFile("f.md", "# Title\n\n```\n# not a heading\n```\n\n# Scene 1: After"));

        Assert.That(adventure.Sections, Has.Count.EqualTo(2));
        Assert.That(adventure.Sections[0].BodyMarkdown, Is.EqualTo("```\n# not a heading\n```"));
    }

    [Test]
    public void TextBeforeTheFirstHeading_BecomesNotesNamedForTheFile()
    {
        var adventure = Import(new MarkdownFile("gm-notes.md", "Bring dice.\n\n# Scene 1: Start\n\nGo."));

        Assert.That(adventure.Title, Is.EqualTo("Scene 1: Start"));
        Assert.That(adventure.Sections[0], Is.EqualTo(new ImportedSection(AdventureSectionKind.Notes, "gm-notes", "Bring dice.")));
    }

    [Test]
    public void AFileWithNoHeadings_IsOneNotesSection_TitledAfterIt()
    {
        var adventure = Import(new MarkdownFile("harbor.md", "Just notes.\r\nTwo lines."));

        Assert.That(adventure.Title, Is.EqualTo("harbor"));
        Assert.That(adventure.Sections, Is.EqualTo(
        [
            new ImportedSection(AdventureSectionKind.Notes, "harbor", "Just notes.\nTwo lines."),
        ]));
    }

    // ------------------------------------------------------- Front matter (feature 32) --

    private const int ThisYear = 2026;

    [Test]
    public void FrontMatter_FillsTheDetails_AndIsNotPartOfAnyBody()
    {
        var adventure = Import(new MarkdownFile("spa.md",
            "---\nsystem: Shadowdark\nedition: \"1st\"\nlevels: 1–3\nyear: 2026\n---\n# The Slime Spa\n\nWelcome.\n"));

        Assert.Multiple(() =>
        {
            Assert.That(adventure.Details,
                Is.EqualTo(new AdventureDetails { GameSystem = "Shadowdark", Edition = "1st", Levels = "1–3", Year = 2026 }));
            Assert.That(adventure.IgnoredDetails, Is.Empty);
            Assert.That(adventure.Title, Is.EqualTo("The Slime Spa"));
            Assert.That(adventure.Sections.Select(s => s.BodyMarkdown), Has.None.Contains("Shadowdark").And.None.Contains("---"));
        });
    }

    [TestCase("game_system")]
    [TestCase("Game-System")]
    [TestCase("SYSTEM")]
    public void TheGameSystemKey_IsMatchedIgnoringCaseDashesAndUnderscores(string key) =>
        Assert.That(Import(new MarkdownFile("spa.md", $"---\n{key}: OSE\n---\n# The Slime Spa\n")).Details.GameSystem,
            Is.EqualTo("OSE"));

    [Test]
    public void TheFixedFields_AndUnknownKeys_AreLeftOutAndReported()
    {
        var adventure = Import(new MarkdownFile("spa.md",
            "---\ntype: Adventure\npublisher: Kobold\nauthors: Someone\nmood: grim\nsystem: OSE\n---\n# The Slime Spa\n"));

        Assert.That(adventure.IgnoredDetails, Is.EqualTo((string[])["type", "publisher", "authors", "mood"]));
        Assert.That(adventure.Details, Is.EqualTo(new AdventureDetails { GameSystem = "OSE" }));
    }

    [TestCase("1066")]
    [TestCase("soon")]
    [TestCase("0")]
    public void ABadYear_IsDroppedAndReported_AndTheImportStillSucceeds(string year)
    {
        var adventure = Import(new MarkdownFile("spa.md", $"---\nedition: 2nd\nyear: {year}\n---\n# The Slime Spa\n"));

        Assert.Multiple(() =>
        {
            Assert.That(adventure.Details, Is.EqualTo(new AdventureDetails { Edition = "2nd" }));
            Assert.That(adventure.IgnoredDetails, Is.EqualTo((string[])["year"]));
        });
    }

    [Test]
    public void AValueOverItsBound_IsDroppedAndReportedByItsKey()
    {
        var adventure = Import(new MarkdownFile("spa.md", $"---\nlevels: {new string('x', 31)}\nyear: 2025\n---\n# The Slime Spa\n"));

        Assert.That(adventure.Details, Is.EqualTo(new AdventureDetails { Year = 2025 }));
        Assert.That(adventure.IgnoredDetails, Is.EqualTo((string[])["levels"]));
    }

    [Test]
    public void AnUnclosedBlock_IsNotFrontMatter_AndStaysInTheText()
    {
        var lines = string.Join('\n', Enumerable.Range(0, AdventureMarkdownImporter.MaxFrontMatterLines).Select(i => $"note {i}"));
        var adventure = Import(new MarkdownFile("spa.md", $"---\nsystem: OSE\n{lines}\n---\n# The Slime Spa\n"));

        Assert.Multiple(() =>
        {
            Assert.That(adventure.Details, Is.EqualTo(new AdventureDetails()));
            Assert.That(adventure.Sections[0].BodyMarkdown, Does.StartWith("---\nsystem: OSE"));
        });
    }

    [Test]
    public void ASecondFilesBlock_IsOrdinaryText()
    {
        var adventure = Import(
            new MarkdownFile("one.md", "# The Slime Spa\n\nWelcome.\n"),
            new MarkdownFile("two.md", "---\nsystem: OSE\n---\n# Scene 1: The Pool\n\nBubbles.\n"));

        Assert.Multiple(() =>
        {
            Assert.That(adventure.Details, Is.EqualTo(new AdventureDetails()));
            Assert.That(adventure.Sections.Any(s => s.BodyMarkdown.Contains("system: OSE")), Is.True);
        });
    }

    [Test]
    public void NoFrontMatter_ImportsAsBefore_WithNoDetails()
    {
        var withNone = Import(new MarkdownFile("two-chapters.md", Fixture()));

        Assert.That(withNone.Details, Is.EqualTo(new AdventureDetails()));
        Assert.That(withNone.IgnoredDetails, Is.Empty);
    }

    [Test]
    public void NoText_IsRefused()
    {
        var (adventure, error) = AdventureMarkdownImporter.Import([new MarkdownFile("empty.md", "  \n")], ThisYear);

        Assert.That(adventure, Is.Null);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void AHeadingLongerThanATitleCanBe_IsRefused_NamingBoth()
    {
        var (adventure, error) = AdventureMarkdownImporter.Import(
            [new MarkdownFile("f.md", "# Title\n\n# " + new string('x', 201))], ThisYear);

        Assert.That(adventure, Is.Null);
        Assert.That(error, Does.Contain("201").And.Contain("200"));
    }
}
