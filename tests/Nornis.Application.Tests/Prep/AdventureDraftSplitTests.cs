using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// Split and gather (feature 37): the headings plan, the plan's rule, the cut and its word check,
/// and gathering back so a gathered draft splits into the same sections.
/// </summary>
[TestFixture]
public class AdventureDraftSplitTests
{
    private const string Draft = """
        The spa opened last spring.
        Nobody who bathed came home.

        # Chapter One: Arrival

        ## Overview of the Spa

        Steam everywhere.

        ## The Baths

        Slimes lurk.

        ```statblock
        # Ooze
        AC 8
        ```

        ## Session clock

        Hour 1: the doors lock.
        """;

    // ----------------------------------------------------------------- planner --

    [Test]
    public void TheHeadingsPlan_StartsAtEachHeading_WithItsKind_AndAnOverviewForTheTextAbove()
    {
        var plan = AdventureDraftPlanner.FromHeadings(Draft);

        Assert.That(plan, Is.EqualTo(
        [
            new DraftSplitPoint(1, "Overview", AdventureSectionKind.Overview),
            new DraftSplitPoint(4, "Chapter One: Arrival", AdventureSectionKind.Chapter),
            new DraftSplitPoint(6, "Overview of the Spa", AdventureSectionKind.Overview),
            new DraftSplitPoint(10, "The Baths", AdventureSectionKind.Scene),
            new DraftSplitPoint(19, "Session clock", AdventureSectionKind.SessionClock),
        ]), "the # inside the stat block's fence doesn't split");
    }

    [Test]
    public void AnUnderlinedHeading_AndAThirdLevelHeading_DontSplit()
    {
        var plan = AdventureDraftPlanner.FromHeadings("## Start\n\nText.\n\nUnderlined\n----------\n\n### Room 1\n\nMore.");

        Assert.That(plan.Select(p => p.StartLine), Is.EqualTo([1]));
    }

    [TestCase("Quick reference: NPCs", AdventureSectionKind.QuickReference)]
    [TestCase("notes for the GM", AdventureSectionKind.Notes)]
    [TestCase("Overview", AdventureSectionKind.Overview)]
    [TestCase("The Drowned Library", AdventureSectionKind.Scene)]
    public void AKind_IsReadFromTheTitle(string title, AdventureSectionKind kind) =>
        Assert.That(AdventureDraftPlanner.KindFromTitle(title), Is.EqualTo(kind));

    // -------------------------------------------------------------- the rule --

    private static readonly string[] FiveLines = ["# A", "text", "## B", "more", "end"];

    private static IEnumerable<TestCaseData> BadPlans()
    {
        yield return new TestCaseData((object)new[] { P(1), P(9) }).SetName("ALinePastTheEnd");
        yield return new TestCaseData((object)new[] { P(0) }).SetName("LineZero");
        yield return new TestCaseData((object)new[] { P(1), P(4), P(3) }).SetName("OutOfOrder");
        yield return new TestCaseData((object)new[] { P(1), P(3), P(3) }).SetName("TheSameLineTwice");
        yield return new TestCaseData((object)new[] { P(2) }).SetName("LeavesOutTextAbove");
        yield return new TestCaseData((object)new[] { new DraftSplitPoint(1, "  ", AdventureSectionKind.Scene) }).SetName("ABlankTitle");
        yield return new TestCaseData((object)new[] { new DraftSplitPoint(1, new string('t', 201), AdventureSectionKind.Scene) }).SetName("ALongTitle");
        yield return new TestCaseData((object)new[] { new DraftSplitPoint(1, "A", (AdventureSectionKind)42) }).SetName("AnUnknownKind");
        yield return new TestCaseData((object)Array.Empty<DraftSplitPoint>()).SetName("NoSections");
    }

    private static DraftSplitPoint P(int line) => new(line, $"Section at {line}", AdventureSectionKind.Scene);

    [TestCaseSource(nameof(BadPlans))]
    public void APlanThatCantBeCut_IsRefused(DraftSplitPoint[] plan) =>
        Assert.That(AdventureDraftPlan.Validate(plan, FiveLines, maxSections: 200), Is.Not.Null);

    [Test]
    public void APlanPastTheSectionCap_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AdventureDraftPlan.Validate([P(1), P(3)], FiveLines, maxSections: 1), Does.Contain("at most 1"));
            Assert.That(AdventureDraftPlan.Validate([P(1), P(3)], FiveLines, maxSections: 2), Is.Null);
        });
    }

    // ------------------------------------------------------------------ cutter --

    [Test]
    public void TheCut_TakesEachHeadingAsItsTitle_AndKeepsEveryOtherLine()
    {
        var cut = AdventureDraftCutter.Cut(Draft, AdventureDraftPlanner.FromHeadings(Draft));

        Assert.Multiple(() =>
        {
            Assert.That(cut.Select(c => c.Title), Is.EqualTo(["Overview", "Chapter One: Arrival", "Overview of the Spa", "The Baths", "Session clock"]));
            Assert.That(cut[0].BodyMarkdown, Is.EqualTo("The spa opened last spring.\nNobody who bathed came home."));
            Assert.That(cut[1].BodyMarkdown, Is.Empty, "a chapter divider with nothing under its heading");
            Assert.That(cut[3].BodyMarkdown, Is.EqualTo("Slimes lurk.\n\n```statblock\n# Ooze\nAC 8\n```"));
        });
    }

    [Test]
    public void ARenamedHeading_StaysInItsBody_SoNoWordLeaves()
    {
        var plan = new[] { new DraftSplitPoint(1, "The Hot Pools", AdventureSectionKind.Scene) };

        var cut = AdventureDraftCutter.Cut("## The Baths\n\nSlimes lurk.", plan);

        Assert.Multiple(() =>
        {
            Assert.That(cut[0].Title, Is.EqualTo("The Hot Pools"));
            Assert.That(cut[0].BodyMarkdown, Is.EqualTo("## The Baths\n\nSlimes lurk."));
        });
    }

    [Test]
    public void ASectionStartingMidText_HoldsTheLinesFromThere()
    {
        var plan = new[] { new DraftSplitPoint(1, "A", AdventureSectionKind.Chapter), P(4) };

        var cut = AdventureDraftCutter.Cut(string.Join('\n', FiveLines), plan);

        Assert.That(cut.Select(c => c.BodyMarkdown), Is.EqualTo(["text\n## B", "more\nend"]),
            "a section may start at any line; the ## B heading that isn't a start stays text");
    }

    [Test]
    public void TheWordCheck_Fails_WhenACutDropsALine()
    {
        // Sabotage: what a cutter that lost "Slimes lurk." would hand the check.
        var pieces = new[] { "## The Baths", "" };

        Assert.Throws<DraftCutCheckFailedException>(() => AdventureDraftCutter.Verify("## The Baths\n\nSlimes lurk.", pieces));
    }

    [Test]
    public void TheWordCheck_Fails_WhenACutChangesOrReordersAWord()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<DraftCutCheckFailedException>(() => AdventureDraftCutter.Verify("Slimes lurk.", ["Slimes lurk!"]));
            Assert.Throws<DraftCutCheckFailedException>(() => AdventureDraftCutter.Verify("one two", ["two", "one"]));
            Assert.DoesNotThrow(() => AdventureDraftCutter.Verify("# One\n\n two  ", ["One", "two"]), "only whitespace and heading marks differ");
        });
    }

    // ---------------------------------------------------------------- gatherer --

    private static AdventureSection Section(int order, AdventureSectionKind kind, string title, string body) =>
        new() { Id = Guid.NewGuid(), Kind = kind, Title = title, BodyMarkdown = body, SortOrder = order };

    [Test]
    public void Gather_JoinsInOrder_WithChapterAndSectionHeadings_LeavingOutTheUnwritten()
    {
        var gathered = AdventureDraftGatherer.Join(
        [
            Section(3, AdventureSectionKind.Scene, "The Baths", "Slimes lurk."),
            Section(1, AdventureSectionKind.Overview, "Overview", "Steam."),
            Section(2, AdventureSectionKind.Chapter, "Chapter 1", ""),
            Section(4, AdventureSectionKind.Notes, "Notes", ""),
            Section(5, AdventureSectionKind.Chapter, "Chapter 2", ""),
        ]);

        Assert.That(gathered, Is.EqualTo("## Overview\n\nSteam.\n\n# Chapter 1\n\n## The Baths\n\nSlimes lurk."),
            "a chapter with written sections under it keeps its heading; an empty one at the end goes");
    }

    [Test]
    public void AGatheredDraft_SplitsBackIntoTheSameSections()
    {
        AdventureSection[] sections =
        [
            Section(1, AdventureSectionKind.Overview, "Overview", "Steam.\n\nMore steam."),
            Section(2, AdventureSectionKind.Chapter, "Chapter One", "Arrival."),
            Section(3, AdventureSectionKind.Scene, "The Baths", "Slimes lurk.\n\n```statblock\n# Ooze\n```"),
            Section(4, AdventureSectionKind.SessionClock, "Session clock", "Hour 1."),
        ];

        var draft = AdventureDraftGatherer.Join(sections);
        var cut = AdventureDraftCutter.Cut(draft, AdventureDraftPlanner.FromHeadings(draft));

        Assert.That(cut, Is.EqualTo(sections.Select(s => new CutSection(s.Kind, s.Title, s.BodyMarkdown))));
    }

    // ------------------------------------------------------------- plan answer --

    [Test]
    public void TheLoremastersAnswer_IsReadAsAPlan_EvenWithWordsAroundIt()
    {
        var plan = AdventureDraftPlanPrompt.ParseAnswer(
            """Here is the plan: [{"startLine": 1, "title": "Overview", "kind": "overview"}, {"startLine": 4, "title": "The Baths", "kind": "Scene"}]""");

        Assert.That(plan, Is.EqualTo(
        [
            new DraftSplitPoint(1, "Overview", AdventureSectionKind.Overview),
            new DraftSplitPoint(4, "The Baths", AdventureSectionKind.Scene),
        ]));
    }

    [TestCase("No plan, sorry.")]
    [TestCase("""[{"startLine": 1, "title": "A", "kind": "Dungeon"}]""")]
    [TestCase("""[{"startLine": 1, "title": "A", "kind": "2"}]""")]
    [TestCase("""[{"startLine": "one", "title": "A", "kind": "Scene"}]""")]
    [TestCase("""[{"startLine": 1, "kind": "Scene"}]""")]
    [TestCase("[1, 2]")]
    [TestCase("[{]")]
    public void AnAnswerThatIsntAPlan_IsNull(string answer) =>
        Assert.That(AdventureDraftPlanPrompt.ParseAnswer(answer), Is.Null);

    [Test]
    public void ThePlanPrompt_NumbersEveryLine_AndCarriesTheBrief()
    {
        var adventure = new Adventure { Id = Guid.NewGuid(), Title = "The Slurping Spa" };
        var lines = AdventureDraftPlan.LinesOf(Draft);

        var prompt = AdventureDraftPlanPrompt.BuildUserMessage(
            new PrepPromptContext("Shadowdark", "1-3", "Four players, two hours.", "WORLD"), adventure, lines);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain(" 1| The spa opened last spring."));
            Assert.That(prompt, Does.Contain($"{lines.Length}| Hour 1: the doors lock."));
            Assert.That(prompt, Does.Contain("Four players, two hours."));
        });
    }
}
