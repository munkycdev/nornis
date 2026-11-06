using Nornis.Application.Prep;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// Suggested edits (feature 37): the one reader of the edit fence, the prose shown around the
/// cards, and Apply, which changes a passage only when it is in the draft exactly once.
/// </summary>
[TestFixture]
public class DraftEditsTests
{
    private const string Answer = """
        I'd make the spa louder.

        ```edit
        <<<
        The spa is quiet.
        Nobody comes.
        ===
        The spa hums with slime.
        >>>
        ```

        Then cut the ending.

        ```edit
        <<<
        The end.
        ===
        >>>
        ```
        """;

    [Test]
    public void Parse_ReadsEachFence_InOrder_WithItsLines()
    {
        var edits = DraftEdits.Parse(Answer);

        Assert.That(edits, Is.EqualTo(
        [
            new DraftEdit("The spa is quiet.\nNobody comes.", "The spa hums with slime."),
            new DraftEdit("The end.", ""),
        ]));
    }

    [Test]
    public void Parse_ReadsWindowsLineEnds()
    {
        var edits = DraftEdits.Parse(Answer.Replace("\n", "\r\n", StringComparison.Ordinal));

        Assert.That(edits[0].Find, Is.EqualTo("The spa is quiet.\nNobody comes."));
    }

    [TestCase("```edit\nThe spa is quiet.\n===\nLoud.\n>>>\n```", TestName = "NoFindMarker")]
    [TestCase("```edit\n<<<\nThe spa is quiet.\n>>>\n```", TestName = "NoDivider")]
    [TestCase("```edit\n<<<\nThe spa is quiet.\n===\nLoud.\n```", TestName = "NoEndMarker")]
    [TestCase("```edit\n<<<\nThe spa is quiet.\n===\nLoud.\n>>>", TestName = "NoClosingFence")]
    [TestCase("```\n<<<\nThe spa is quiet.\n===\nLoud.\n>>>\n```", TestName = "NotAnEditFence")]
    public void AFenceOutOfShape_IsNoEdit_AndStaysInTheProse(string answer)
    {
        Assert.That(DraftEdits.Parse(answer), Is.Empty);
        Assert.That(DraftEdits.ProseOf(answer), Is.Null, "with no edits there is no separate prose");
    }

    [Test]
    public void ProseOf_TakesTheFencesOut_AndKeepsTheWords()
    {
        Assert.That(DraftEdits.ProseOf(Answer), Is.EqualTo("I'd make the spa louder.\n\nThen cut the ending."));
    }

    [Test]
    public void Apply_ReplacesThePassage_FoundOnce()
    {
        var draft = "# Spa\n\nThe spa is quiet.\nNobody comes.\n\nThe end.";

        var applied = DraftEdits.Apply(draft, DraftEdits.Parse(Answer)[0]);

        Assert.That(applied, Is.EqualTo("# Spa\n\nThe spa hums with slime.\n\nThe end."));
    }

    [Test]
    public void Apply_RefusesAPassage_FoundTwice_OrNowhere()
    {
        var edit = new DraftEdit("The end.", "Fin.");

        Assert.Multiple(() =>
        {
            Assert.That(DraftEdits.Apply("The end.\n\nThe end.", edit), Is.Null);
            Assert.That(DraftEdits.Apply("Something else.", edit), Is.Null);
        });
    }

    [Test]
    public void Apply_MatchesADraftStoredWithWindowsLineEnds()
    {
        var applied = DraftEdits.Apply("The spa is quiet.\r\nNobody comes.", DraftEdits.Parse(Answer)[0]);

        Assert.That(applied, Is.EqualTo("The spa hums with slime."));
    }

    [Test]
    public void AnEmptyPassage_IsTheWholeDraft_EmptyOrNot()
    {
        // Feature 41: "regenerate the whole adventure" replaces a draft that has text.
        var edit = new DraftEdit("", "# The Spa");

        Assert.Multiple(() =>
        {
            Assert.That(DraftEdits.Apply("", edit), Is.EqualTo("# The Spa"));
            Assert.That(DraftEdits.Apply("  \n", edit), Is.EqualTo("# The Spa"));
            Assert.That(DraftEdits.Apply("Already written.", edit), Is.EqualTo("# The Spa"));
        });
    }

    // ------------------------------------------------------------ feature 41 --

    private const string FourTicks = """
        This gives you a complete first draft.

        ````edit
        <<<
        ===
        # The Great Toilet Heist

        ```statblock
        # Muck Scavenger
        Armor Class: 13
        ```

        The end.
        >>>
        ````
        """;

    [Test]
    public void AFourBacktickFence_IsAnEdit_WithAStatBlockFenceInside()
    {
        // The 2026-11-06 answer used four backticks so its stat blocks could nest; three-only
        // parsing read it as prose, so there was nothing to apply even before it was cut off.
        var edits = DraftEdits.Parse(FourTicks);

        Assert.Multiple(() =>
        {
            Assert.That(edits, Has.Count.EqualTo(1));
            Assert.That(edits[0].Find, Is.Empty);
            Assert.That(edits[0].Replace, Does.StartWith("# The Great Toilet Heist").And.Contain("```statblock\n# Muck Scavenger").And.EndWith("The end."));
            Assert.That(DraftEdits.ProseOf(FourTicks), Is.EqualTo("This gives you a complete first draft."));
        });
    }

    [Test]
    public void AThreeBacktickFence_MayHoldAStatBlockToo()
    {
        var answer = "```edit\n<<<\n===\n```statblock\n# Slime\n```\n>>>\n```";

        Assert.That(DraftEdits.Parse(answer).Single().Replace, Is.EqualTo("```statblock\n# Slime\n```"));
    }

    [Test]
    public void TheClosingLine_MustMatchTheOpeningBackticks()
    {
        Assert.That(DraftEdits.Parse("````edit\n<<<\n===\nLoud.\n>>>\n```"), Is.Empty);
    }

    [Test]
    public void AnEditThatNeverEnds_DoesNotSwallowTheNextOne()
    {
        var answer = "```edit\n<<<\nA\n===\nB\n\n```edit\n<<<\nC\n===\nD\n>>>\n```";

        Assert.That(DraftEdits.Parse(answer), Is.EqualTo([new DraftEdit("C", "D")]));
    }

    [TestCase("````edit", "````")]
    [TestCase("```edit", "```")]
    [TestCase("`````edit", "`````")]
    [TestCase("``edit", null)]
    [TestCase("```statblock", null)]
    [TestCase("````edits", null)]
    public void OpeningTicks_NamesAnEditFencesBackticks(string line, string? expected) =>
        Assert.That(DraftEdits.OpeningTicks(line), Is.EqualTo(expected));

    [Test]
    public void ApplyAll_AppliesInOrder_AndSkipsWhatDoesNotFit()
    {
        var (draft, outcomes) = DraftEdits.ApplyAll("Pell is a plumber.\nTilda lost a locket.",
        [
            new DraftEdit("Pell is a plumber.", "Pell is a grumpy plumber."),
            new DraftEdit("Nobody wrote this.", "Gone."),
            new DraftEdit("Pell is a grumpy plumber.", "Pell is a very grumpy plumber."),
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(draft, Is.EqualTo("Pell is a very grumpy plumber.\nTilda lost a locket."), "each edit sees the one before");
            Assert.That(outcomes, Is.EqualTo([DraftEditOutcome.Applied, DraftEditOutcome.DidNotFit, DraftEditOutcome.Applied]));
        });
    }

    [TestCase("Prose only.", false)]
    [TestCase(FourTicks, false)]
    [TestCase("Draft:\n\n````edit\n<<<\n===\n# Heist\n```statblock\n# Grand Flusher\nSpeed:", true)]
    [TestCase("```edit\n<<<\n===\nHalf a", true)]
    public void HasUnclosedFence_SpotsAnAnswerThatStoppedInsideAnEdit(string answer, bool expected) =>
        Assert.That(DraftEdits.HasUnclosedFence(answer), Is.EqualTo(expected));

    [Test]
    public void Describe_SaysWhatChanged_NeverTheText()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DraftEdits.Describe(new DraftEdit("", new string('x', 8_176)), DraftEditOutcome.Applied),
                Is.EqualTo("[Edit to the draft: wrote the whole draft, 8176 characters (applied)]"));
            Assert.That(DraftEdits.Describe(new DraftEdit("Pell is a plumber.", "Pell is grumpy."), DraftEditOutcome.DidNotFit),
                Is.EqualTo("[Edit to the draft: replaced \"Pell is a plumber.\" with 15 characters starting \"Pell is grumpy.\" (didn't fit, not applied)]"));
            Assert.That(DraftEdits.Describe(new DraftEdit("The end.", ""), null),
                Is.EqualTo("[Edit to the draft: took out \"The end.\"]"));
        });
    }
}
