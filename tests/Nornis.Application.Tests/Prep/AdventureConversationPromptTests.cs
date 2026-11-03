using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// The side conversation's prompt (feature 30, design "The prompt"): cache order, the section on
/// screen cut to its bound, the transcript's labels, the markup conventions named, and the
/// history window taking whole messages only.
/// </summary>
[TestFixture]
public class AdventureConversationPromptTests
{
    private static Adventure AnAdventure(out AdventureSection scene)
    {
        var adventure = new Adventure { Id = Guid.NewGuid(), Title = "The Silver Key", Pitch = "A heist in a drowned city." };
        scene = new AdventureSection
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = AdventureSectionKind.Scene,
            Title = "The Lighthouse",
            BodyMarkdown = "Mirela Thorn waits at the lighthouse.",
            SortOrder = 2,
        };
        adventure.Sections =
        [
            new AdventureSection
            {
                Id = Guid.NewGuid(),
                AdventureId = adventure.Id,
                Kind = AdventureSectionKind.Overview,
                Title = "Overview",
                BodyMarkdown = "",
                SortOrder = 1,
            },
            scene,
        ];
        return adventure;
    }

    private static ConversationMessage Message(ConversationRole role, string content) =>
        new() { Id = Guid.NewGuid(), Role = role, Content = content };

    private static string Build(Adventure adventure, Guid? sectionId, IReadOnlyList<ConversationMessage> history, int maxSectionChars = 6_000) =>
        AdventureConversationPrompt.BuildUserMessage(new AdventureConversationInput(
            new PrepPromptContext("Fate Accelerated", adventure.Details.Levels, null, "WORLD-CONTEXT"), adventure, sectionId, history, "THE-NEW-MESSAGE", maxSectionChars));

    [Test]
    public void ThePartsComeInCacheOrder_LeastChangingFirst()
    {
        var adventure = AnAdventure(out var scene);

        var prompt = Build(adventure, scene.Id, [Message(ConversationRole.User, "EARLIER-QUESTION")]);

        var order = new[] { "WORLD-CONTEXT", "## The adventure: The Silver Key", "### Outline", "## The section the GM has open",
            "EARLIER-QUESTION", "THE-NEW-MESSAGE" }.Select(p => prompt.IndexOf(p, StringComparison.Ordinal)).ToList();
        Assert.That(order, Is.All.GreaterThanOrEqualTo(0));
        Assert.That(order, Is.Ordered);
    }

    [Test]
    public void InDesignMode_TheWholeDraftRides_InsteadOfTheSection()
    {
        var adventure = AnAdventure(out var scene);
        adventure.DraftMarkdown = "# The Silver Key\r\n\r\nDRAFT-START " + new string('x', 20_000) + " DRAFT-END";

        var prompt = AdventureConversationPrompt.BuildUserMessage(new AdventureConversationInput(
            new PrepPromptContext("Fate Accelerated", null, null, "WORLD-CONTEXT"), adventure, scene.Id, [], "THE-NEW-MESSAGE",
            MaxSectionChars: 100, OnDraft: true));

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain("## The design draft the GM is working on"));
            Assert.That(prompt, Does.Contain("DRAFT-END"), "the draft is never cut: an edit must quote it exactly");
            Assert.That(prompt, Does.Not.Contain("\r\n"), "line ends are the ones Apply matches against");
            Assert.That(prompt, Does.Not.Contain("## The section the GM has open"));
            Assert.That(prompt.IndexOf("DRAFT-START", StringComparison.Ordinal), Is.LessThan(prompt.IndexOf("THE-NEW-MESSAGE", StringComparison.Ordinal)));
        });
    }

    [Test]
    public void InDesignMode_AnEmptyDraft_IsSaidToBeEmpty()
    {
        var adventure = AnAdventure(out _);

        var prompt = AdventureConversationPrompt.BuildUserMessage(new AdventureConversationInput(
            new PrepPromptContext(null, null, null, "WORLD-CONTEXT"), adventure, null, [], "Write it.", 100, OnDraft: true));

        Assert.That(prompt, Does.Contain("The draft is empty."));
    }

    [Test]
    public void TheSystemPrompt_TeachesTheEditFence_WhichTheParserReads()
    {
        Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain(DraftEdits.ConventionForPrompt));
        Assert.That(DraftEdits.Parse(DraftEdits.ConventionForPrompt), Has.Count.EqualTo(1), "the example the prompt teaches is an edit");
    }

    [Test]
    public void TheTranscript_LabelsTheGmAndTheLoremaster()
    {
        var adventure = AnAdventure(out _);

        var prompt = Build(adventure, null,
            [Message(ConversationRole.User, "Who else?"), Message(ConversationRole.Assistant, "The harbormaster.")]);

        Assert.That(prompt, Does.Contain("GM: Who else?"));
        Assert.That(prompt, Does.Contain("Loremaster: The harbormaster."));
    }

    [Test]
    public void WithNoSectionOpen_NoSectionIsShown_AndTheOutlineMarksNone()
    {
        var adventure = AnAdventure(out _);

        var prompt = Build(adventure, null, []);

        Assert.That(prompt, Does.Not.Contain("## The section the GM has open"));
        Assert.That(prompt, Does.Not.Contain("open on the GM's screen"));
        Assert.That(prompt, Does.Contain("This is the first message."));
    }

    [Test]
    public void TheSectionOnScreen_IsCutToItsBound()
    {
        var adventure = AnAdventure(out var scene);
        scene.BodyMarkdown = new string('a', 30) + "TAIL";

        var prompt = Build(adventure, scene.Id, [], maxSectionChars: 30);

        Assert.That(prompt, Does.Contain(new string('a', 30) + "\n[…cut]"));
        Assert.That(prompt, Does.Not.Contain("TAIL"));
        Assert.That(prompt, Does.Contain("[Scene] The Lighthouse (written)  <- open on the GM's screen"));
    }

    [Test]
    public void TheSystemPrompt_NamesThePrepMarkupConventions_AndTheHiddenRule()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain(PrepMarkup.PropMarker));
            Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain(PrepMarkup.AskMarker));
            Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain(PrepMarkup.StatBlockInfo));
            Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain("Hidden"));
            Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain("[ref:"));
        });
    }

    [Test]
    public void TheHistoryWindow_TakesTheNewestWholeMessages_OldestFirst()
    {
        var earlier = new[]
        {
            Message(ConversationRole.User, "aaaaaaaaaa"),      // 10
            Message(ConversationRole.Assistant, "bbbbbbbbbbbbbbbbbbbb"), // 20
            Message(ConversationRole.User, "ccccc"),           // 5
            Message(ConversationRole.Assistant, "dddddddddd"), // 10
        };

        var window = AdventureConversationPrompt.HistoryWindow(earlier, 25);

        Assert.That(window.Select(m => m.Content), Is.EqualTo(["ccccc", "dddddddddd"]),
            "the 20-character answer would cross 25, so the window ends there even though the 10 before it would fit");
    }

    [Test]
    public void TheHistoryWindow_IsEmpty_WhenTheNewestMessageAloneIsTooLong()
    {
        var window = AdventureConversationPrompt.HistoryWindow([Message(ConversationRole.Assistant, new string('x', 50))], 49);

        Assert.That(window, Is.Empty);
    }
}
