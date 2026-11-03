using Nornis.Application.Prep;
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
    public void AnEmptyPassage_FillsAnEmptyDraft_AndNothingElse()
    {
        var edit = new DraftEdit("", "# The Spa");

        Assert.Multiple(() =>
        {
            Assert.That(DraftEdits.Apply("", edit), Is.EqualTo("# The Spa"));
            Assert.That(DraftEdits.Apply("  \n", edit), Is.EqualTo("# The Spa"));
            Assert.That(DraftEdits.Apply("Already written.", edit), Is.Null);
        });
    }
}
