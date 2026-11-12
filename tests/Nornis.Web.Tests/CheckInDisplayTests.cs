using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>The words the check-in shows (feature 44) for what the API sends as names.</summary>
[TestFixture]
public class CheckInDisplayTests
{
    private static CheckInQuestionDto Question(string kind, CheckInEvidenceDto evidence) => new(
        Guid.NewGuid(), 0, true, kind, Guid.NewGuid(), "Subject", "Text?", null, evidence,
        null, null, [], null, null, null, null);

    private static CheckInEvidenceDto Evidence(
        string? status = null, int facts = 0, int sources = 0, int quiet = 0, int open = 0,
        string? firstSeen = null, DateTimeOffset? firstSeenAt = null, string? predicate = null, int artifacts = 0) =>
        new(null, status, facts, sources, quiet, open, firstSeen, firstSeenAt, [], [], predicate, artifacts, []);

    [TestCase("Stop", "Stop recording")]
    [TestCase("Pursuing", "Pursuing")]
    [TestCase("Skip", "Skip")]
    public void AnswerLabel_RenamesOnlyStop(string answer, string expected) =>
        Assert.That(CheckInDisplay.AnswerLabel(answer), Is.EqualTo(expected));

    [Test]
    public void AnswerHint_SaysWhatEachAnswerDoes()
    {
        Assert.That(CheckInDisplay.AnswerHint("Storyline", "Done"), Does.Contain("Resolved"));
        Assert.That(CheckInDisplay.AnswerHint("Entry", "Archive"), Does.Contain("restore"));
        Assert.That(CheckInDisplay.AnswerHint("Pattern", "Skip"), Does.Contain("another time"));
        Assert.That(CheckInDisplay.AnswerHint("Pattern", "Done"), Is.Empty);
    }

    [Test]
    public void EvidenceLine_Storyline_SaysStatusQuietOpenAndProvenance()
    {
        var line = CheckInDisplay.EvidenceLine(Question("Storyline", Evidence(status: "Active", facts: 3, sources: 1, quiet: 4, open: 1)));

        Assert.That(line, Is.EqualTo("Active · 4 sessions quiet · 1 open question · 3 facts from 1 source"));
    }

    [Test]
    public void EvidenceLine_Entry_SaysWhereItWasFirstSeen()
    {
        var at = new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        var line = CheckInDisplay.EvidenceLine(Question("Entry", Evidence(facts: 1, sources: 1, firstSeen: "The first night", firstSeenAt: at)));

        Assert.That(line, Is.EqualTo("1 fact from 1 source · first seen in “The first night” (8 Oct 2026)"));
    }

    [Test]
    public void EvidenceLine_Pattern_CountsEntriesAndFacts()
    {
        var line = CheckInDisplay.EvidenceLine(Question("Pattern", Evidence(facts: 8, predicate: "eye colour", artifacts: 3)));

        Assert.That(line, Is.EqualTo("“eye colour” on 3 entries, 8 facts in all"));
    }

    [Test]
    public void Count_Pluralises()
    {
        Assert.That(CheckInDisplay.Count(1, "session"), Is.EqualTo("1 session"));
        Assert.That(CheckInDisplay.Count(2, "session"), Is.EqualTo("2 sessions"));
        Assert.That(CheckInDisplay.Count(2, "entry", "entries"), Is.EqualTo("2 entries"));
    }
}
