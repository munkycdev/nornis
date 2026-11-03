using Nornis.Application.Prep;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// Prep's game-block grammar (feature 35, Requirement 1): one row of the design's grammar table
/// per test, the legacy <c>Name:</c> entry, and the export line Export to Nornis files as one fact.
/// </summary>
[TestFixture]
public class GameBlocksTests
{
    private static GameBlock Stat(string text) => GameBlocks.Parse(GameBlockKind.Stat, text.Split('\n'));

    private static GameBlock Item(string text) => GameBlocks.Parse(GameBlockKind.Item, text.Split('\n'));

    private const string Voss = """
        # Captain Voss
        *Medium human, pirate captain*
        AC: 15
        HP: 27 (5d8+5)
        Knows every tide
        ## Traits
        Sea legs: Advantage on saves against being knocked prone.
        ## Actions
        Cutlass: +5 to hit, 1d8+3 slashing.
        """;

    private const string Cloak = """
        # Cloak of the Tidecaller
        *Wondrous item, rare*
        Weight: 1 lb
        Value: 400 gp
        ---
        While you wear it you breathe water.
        Note: the clasp is cold.
        """;

    // ------------------------------------------------------------------ Grammar --

    [Test]
    public void AStatBlock_HasItsName_Subtitle_EntriesAndGroups()
    {
        var block = Stat(Voss);

        Assert.Multiple(() =>
        {
            Assert.That(block.Kind, Is.EqualTo(GameBlockKind.Stat));
            Assert.That(block.Name, Is.EqualTo("Captain Voss"));
            Assert.That(block.Subtitle, Is.EqualTo("Medium human, pirate captain"));
            Assert.That(block.Groups.Select(g => g.Heading), Is.EqualTo((string?[])[null, "Traits", "Actions"]));
            Assert.That(block.Groups[0].Lines, Is.EqualTo((GameBlockLine[])
            [
                new("AC", "15"),
                new("HP", "27 (5d8+5)"),
                new(null, "Knows every tide"),
            ]));
            Assert.That(block.Groups[2].Lines, Is.EqualTo((GameBlockLine[])[new("Cutlass", "+5 to hit, 1d8+3 slashing.")]));
            Assert.That(block.Description, Is.Empty);
        });
    }

    [Test]
    public void AnItem_EndsItsEntriesAtTheRule_AndTheRestIsDescription_ColonsAndAll()
    {
        var block = Item(Cloak);

        Assert.Multiple(() =>
        {
            Assert.That(block.Name, Is.EqualTo("Cloak of the Tidecaller"));
            Assert.That(block.Subtitle, Is.EqualTo("Wondrous item, rare"));
            Assert.That(block.Lines, Is.EqualTo((GameBlockLine[])[new("Weight", "1 lb"), new("Value", "400 gp")]));
            Assert.That(block.Description, Is.EqualTo((string[])["While you wear it you breathe water.", "Note: the clasp is cold."]));
        });
    }

    [Test]
    public void EachKind_HasOnlyTheStructureItNeeds()
    {
        var item = Item("# Rope\n## Uses\nLength: 50 ft");
        var stat = Stat("# Guard\nAC: 12\n---\nHP: 9");

        Assert.Multiple(() =>
        {
            Assert.That(item.Groups, Has.Count.EqualTo(1), "an item has no groups");
            Assert.That(item.Lines.First(), Is.EqualTo(new GameBlockLine(null, "## Uses")));
            Assert.That(stat.Description, Is.Empty, "a stat block has no description");
            Assert.That(stat.Lines, Has.Member(new GameBlockLine(null, "---")).And.Member(new GameBlockLine("HP", "9")));
        });
    }

    [Test]
    public void ASubtitle_OnlyFollowsTheName()
    {
        var noName = Stat("*Not a subtitle*\nAC: 12");
        var late = Stat("# Guard\nAC: 12\n*Not a subtitle*");
        var bold = Stat("# Guard\n**Bold, not a subtitle**");

        Assert.Multiple(() =>
        {
            Assert.That(noName.Subtitle, Is.Null);
            Assert.That(noName.Lines.First(), Is.EqualTo(new GameBlockLine(null, "*Not a subtitle*")));
            Assert.That(late.Subtitle, Is.Null);
            Assert.That(bold.Subtitle, Is.Null);
        });
    }

    [Test]
    public void AHash_AfterTheFirstLine_IsText()
    {
        var block = Stat("AC: 12\n# Not a name");

        Assert.Multiple(() =>
        {
            Assert.That(block.Name, Is.Null);
            Assert.That(block.Lines.Last(), Is.EqualTo(new GameBlockLine(null, "# Not a name")));
        });
    }

    [Test]
    public void ALineStartingWithAColon_IsText_NotAnEntryWithNoLabel()
    {
        Assert.That(Stat(":15").Lines.Single(), Is.EqualTo(new GameBlockLine(null, ":15")));
    }

    [Test]
    public void BlankLines_AndSurroundingSpace_AreIgnored()
    {
        var block = Stat("\n   # Guard  \n\n  AC :  12  \n\n");

        Assert.Multiple(() =>
        {
            Assert.That(block.Name, Is.EqualTo("Guard"));
            Assert.That(block.Lines.Single(), Is.EqualTo(new GameBlockLine("AC", "12")));
        });
    }

    [Test]
    public void AnEmptyBlock_IsEmpty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Stat("").IsEmpty, Is.True);
            Assert.That(Stat("\n \n").IsEmpty, Is.True);
            Assert.That(Stat("AC: 12").IsEmpty, Is.False);
            Assert.That(Item("# Rope").IsEmpty, Is.False);
        });
    }

    // ------------------------------------------------------------------- Legacy --

    [Test]
    public void ABlockWrittenBeforeTheGrammar_KeepsItsLines_AndItsNameEntryNamesIt()
    {
        var block = Stat("Name: Captain Voss\nHP: 27\nSpeed: 30 ft., swim 20 ft.\nKnows every tide");

        Assert.Multiple(() =>
        {
            Assert.That(block.Name, Is.EqualTo("Captain Voss"));
            Assert.That(block.Lines, Is.EqualTo((GameBlockLine[])
            [
                new("HP", "27"),
                new("Speed", "30 ft., swim 20 ft."),
                new(null, "Knows every tide"),
            ]));
        });
    }

    [Test]
    public void OnlyAFirstNameEntry_Names_AndAnEmptyOneIsKept()
    {
        var late = Stat("HP: 27\nName: Captain Voss");
        var blank = Stat("Name: \nHP: 27");

        Assert.Multiple(() =>
        {
            Assert.That(late.Name, Is.Null);
            Assert.That(late.Lines, Has.Member(new GameBlockLine("Name", "Captain Voss")));
            Assert.That(blank.Name, Is.Null);
            Assert.That(blank.Lines.First(), Is.EqualTo(new GameBlockLine("Name", "")));
        });
    }

    // ----------------------------------------------------------- In a section --

    [Test]
    public void InMarkdown_FindsEveryGameBlock_InOrder_AndNothingElse()
    {
        var markdown = "Intro.\n\n```statblock\n# Voss\nAC: 15\n```\n\n```csharp\nvar x = 1;\n```\n\n> Read aloud.\n\n"
            + "- a list\n\n  ```item\n  # Rope\n  ```\n";

        var blocks = GameBlocks.InMarkdown(markdown);

        Assert.That(blocks.Select(b => (b.Kind, b.Name)), Is.EqualTo(((GameBlockKind, string?)[])
        [
            (GameBlockKind.Stat, "Voss"),
            (GameBlockKind.Item, "Rope"),
        ]));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void InMarkdown_OfNothing_IsEmpty(string? markdown) =>
        Assert.That(GameBlocks.InMarkdown(markdown), Is.Empty);

    [TestCase("statblock", GameBlockKind.Stat)]
    [TestCase("item", GameBlockKind.Item)]
    [TestCase(" item ", GameBlockKind.Item)]
    [TestCase("Item", null)]
    [TestCase("csharp", null)]
    [TestCase(null, null)]
    public void TheInfoString_DecidesTheKind(string? info, GameBlockKind? kind) =>
        Assert.That(GameBlocks.KindOf(info), Is.EqualTo(kind));

    // ------------------------------------------------------------- Export line --

    [Test]
    public void AStatBlock_ExportsAsOneLine_NamingItsSystem()
    {
        var line = GameBlocks.ExportLineFor(Stat(Voss), "Shadowdark")!;

        Assert.Multiple(() =>
        {
            Assert.That(line.Text, Is.EqualTo(
                "Stat block for \"Captain Voss\" (Shadowdark): *Medium human, pirate captain*. AC: 15; HP: 27 (5d8+5); Knows every tide."
                + " Traits — Sea legs: Advantage on saves against being knocked prone."
                + " Actions — Cutlass: +5 to hit, 1d8+3 slashing."));
            Assert.That(line.WasCut, Is.False);
            Assert.That(line.Text, Does.Not.Contain("\n"), "one line, so the extractor reads one fact");
        });
    }

    [Test]
    public void AnItem_ExportsWithItsDescription()
    {
        var line = GameBlocks.ExportLineFor(Item(Cloak), null)!;

        Assert.That(line.Text, Is.EqualTo(
            "Item \"Cloak of the Tidecaller\": *Wondrous item, rare*. Weight: 1 lb; Value: 400 gp."
            + " While you wear it you breathe water. Note: the clasp is cold."));
    }

    [Test]
    public void AnUnnamedBlock_HasNoExportLine() =>
        Assert.That(GameBlocks.ExportLineFor(Stat("AC: 15"), "Shadowdark"), Is.Null);

    [Test]
    public void AnEmptyGroup_LeavesNoTrace() =>
        Assert.That(GameBlocks.ExportLineFor(Stat("# Guard\nAC: 12\n## Actions"), null)!.Text,
            Is.EqualTo("Stat block for \"Guard\": AC: 12."));

    [Test]
    public void ALongBlock_IsCutAfterTheLastWholeEntry_WithinTheFactLimit()
    {
        var entries = string.Join('\n', Enumerable.Range(1, 200).Select(i => $"Trait {i:D3}: {new string('x', 20)}"));
        var line = GameBlocks.ExportLineFor(Stat("# Hydra\n" + entries), null)!;

        Assert.Multiple(() =>
        {
            Assert.That(line.WasCut, Is.True);
            Assert.That(line.Text.Length, Is.LessThanOrEqualTo(GameBlocks.MaxExportLineChars));
            Assert.That(line.Text, Does.EndWith("; …"), "it ends after a whole entry");
            Assert.That(line.Text, Does.Contain("Trait 001: "));
        });
    }

    [Test]
    public void ABlockThatFitsExactly_IsNotCut_AndOneMoreCharacterIs()
    {
        var head = "Stat block for \"G\": ";
        var fits = GameBlocks.MaxExportLineChars - GameBlocks.SystemReserveChars - head.Length - "A: .".Length;
        var exact = GameBlocks.ExportLineFor(Stat($"# G\nA: {new string('x', fits)}"), null)!;
        var over = GameBlocks.ExportLineFor(Stat($"# G\nA: {new string('x', fits + 1)}"), null)!;

        Assert.Multiple(() =>
        {
            Assert.That(exact.Text.Length, Is.EqualTo(GameBlocks.MaxExportLineChars - GameBlocks.SystemReserveChars));
            Assert.That(exact.WasCut, Is.False);
            Assert.That(over.WasCut, Is.True);
        });
    }

    [Test]
    public void TheSystem_NeverDecidesACut_AndTheLongestStillFits()
    {
        var head = "Stat block for \"G\": ";
        var fits = GameBlocks.MaxExportLineChars - GameBlocks.SystemReserveChars - head.Length - "A: .".Length;
        var block = Stat($"# G\nA: {new string('x', fits)}");
        var longest = new string('s', 200) + " " + new string('e', 50);

        var line = GameBlocks.ExportLineFor(block, longest)!;

        Assert.Multiple(() =>
        {
            Assert.That(line.WasCut, Is.False, "the warning runs with no system and must agree with the export");
            Assert.That(line.Text.Length, Is.EqualTo(GameBlocks.MaxExportLineChars));
            Assert.That(GameBlocks.ExportLineFor(block, new string('s', 900))!.Text.Length,
                Is.LessThanOrEqualTo(GameBlocks.MaxExportLineChars), "a system past the columns' widths is cut, not trusted");
        });
    }

    [Test]
    public void AHugeName_CannotFillTheLine()
    {
        var line = GameBlocks.ExportLineFor(Stat("# " + new string('n', 5_000) + "\nAC: 12"), "Shadowdark")!;

        Assert.Multiple(() =>
        {
            Assert.That(line.Text.Length, Is.LessThanOrEqualTo(GameBlocks.MaxExportLineChars));
            Assert.That(line.Text, Does.Contain("AC: 12"));
        });
    }
}
