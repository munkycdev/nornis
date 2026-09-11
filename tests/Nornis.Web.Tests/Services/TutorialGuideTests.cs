using Nornis.Web.Navigation;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

/// <summary>
/// The tutorial follows the sidebar. On 2026-09-10 the checklist still read "Click Locations in
/// the sidebar" a day after that entry became Map under World: its hints were prose copies of a
/// table that had moved, and nothing could fail. These pin the construction that replaced them —
/// where a step is done is looked up in <see cref="NavGroups"/>, never written — and the two
/// properties the lookup has to hold: every step's page has a door, and a step done in player
/// view leads somewhere a player can go.
/// </summary>
[TestFixture]
public class TutorialGuideTests
{
    private static IEnumerable<TutorialStep> StepsWithAPage => TutorialGuide.Steps.Where(s => s.Href is not null);

    [Test]
    public void EveryStepWithAPage_HasADoorInTheSidebar()
    {
        foreach (var step in StepsWithAPage)
        {
            Assert.That(TutorialGuide.Door(step), Is.Not.Null,
                $"'{step.Title}' leads to {step.Href}, which no sidebar entry owns — the tutorial has drifted from the navigation.");
        }
    }

    [Test]
    public void StepsDoneAsAPlayer_NeverLeadIntoTheGmGroup()
    {
        foreach (var step in StepsWithAPage.Where(s => s.InPlayerView))
        {
            Assert.That(TutorialGuide.Door(step)!.Group.GmOnly, Is.False,
                $"'{step.Title}' is done while viewing as player but leads into the GM group, which player view hides.");
        }
    }

    [Test]
    public void Hints_NeverDescribeTheSidebar()
    {
        foreach (var step in TutorialGuide.Steps)
        {
            Assert.That(step.Hint, Does.Not.Contain("sidebar").IgnoreCase,
                $"'{step.Title}' writes the sidebar into its hint; the where-line is read from NavGroups instead.");
            Assert.That(step.Hint, Does.Not.StartWith("Click "),
                $"'{step.Title}' opens with a click instruction — the title is already the link.");
        }
    }

    [Test]
    public void Hints_NamePagesByTheirCurrentSidebarLabel()
    {
        foreach (var step in TutorialGuide.Steps)
        {
            Assert.That(TutorialGuide.HintFor(step), Does.Not.Contain("{/"),
                $"'{step.Title}' names a page the sidebar does not know: {step.Hint}");
        }

        // The labels come from NavGroups, so renaming an entry there renames it here.
        var watch = TutorialGuide.Find(TutorialGuide.WatchExtraction)!;
        var sources = NavGroups.FindByPath("/sources")!.Label;
        Assert.That(TutorialGuide.HintFor(watch), Does.Contain($"{sources} wears a count"));

        var reveal = TutorialGuide.Find(TutorialGuide.RevealSecret)!;
        Assert.That(TutorialGuide.HintFor(reveal), Does.StartWith(NavGroups.FindByPath("/convergence")!.Label));
    }

    [Test]
    public void Door_ReadsTheGroupAndTheEntry()
    {
        var door = TutorialGuide.Door(TutorialGuide.Find(TutorialGuide.StandSomewhere)!)!;

        Assert.That(door.Group.Label, Is.EqualTo("World"));
        Assert.That(door.Item.Label, Is.EqualTo("Map"));
    }

    [Test]
    public void StepAt_MatchesThePageAndWhatIsUnderIt_ForTheTriggerAsked()
    {
        Assert.That(TutorialGuide.StepAt("/codex", TutorialTrigger.Visit)?.Key, Is.EqualTo(TutorialGuide.MeetTheCast));
        Assert.That(TutorialGuide.StepAt($"/codex/{Guid.NewGuid()}", TutorialTrigger.Visit)?.Key, Is.EqualTo(TutorialGuide.MeetTheCast));
        Assert.That(TutorialGuide.StepAt($"/campaigns/{Guid.NewGuid()}", TutorialTrigger.Visit)?.Key, Is.EqualTo(TutorialGuide.OpenTheCampaign));

        // A visit to Capture no longer completes anything: chapter two's Capture step is state-detected.
        Assert.That(TutorialGuide.StepAt("/capture", TutorialTrigger.Visit), Is.Null);

        // The closer is not a plain visit — it asks for player view and a reveal first.
        Assert.That(TutorialGuide.StepAt("/learned", TutorialTrigger.Visit), Is.Null);
        Assert.That(TutorialGuide.StepAt("/learned", TutorialTrigger.LearnedAsPlayer)?.Key, Is.EqualTo(TutorialGuide.SeeWhatTheySee));

        // /codexx is not under /codex.
        Assert.That(TutorialGuide.StepAt("/codexx", TutorialTrigger.Visit), Is.Null);
    }

    [Test]
    public void Keys_AreUnique_AndAnUnknownKeyIsNull()
    {
        var keys = TutorialGuide.Steps.Select(s => s.Key).ToList();

        Assert.That(keys, Is.Unique);
        Assert.That(TutorialGuide.Find("visit-capture"), Is.Null, "retired 2026-09-10; a server still sending it renders by key");
    }
}
