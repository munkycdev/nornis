using Nornis.Application.Ai;
using Nornis.Application.Models;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Ai;

/// <summary>
/// The standing instructions (feature 44) reach two prompts through one renderer: the extraction
/// system prompt, where they sit between the purpose and the rules, and the check-in chooser's
/// message, so it does not ask what the GM has settled. A world without any sees no section.
/// </summary>
[TestFixture]
public class EditorialBriefPromptTests
{
    private static readonly ExtractionRequest Bare = new()
    {
        SourceBody = "The party bought rope.",
        SourceTitle = "Session 2",
        SourceType = "SessionNote",
        SourceVisibility = "PartyVisible",
    };

    [Test]
    public void Render_Empty_IsEmpty() =>
        Assert.That(EditorialBriefPrompt.Render([]), Is.Empty);

    [Test]
    public void Render_NumbersTheRules_UnderTheHeading_WithPrecedenceStated()
    {
        var text = EditorialBriefPrompt.Render(["  Shop inventories are background. ", "Track every named ship."]);

        Assert.That(text, Does.StartWith(EditorialBriefPrompt.Heading));
        Assert.That(text, Does.Contain("take precedence"));
        Assert.That(text, Does.Contain("1. Shop inventories are background.\n2. Track every named ship."));
        Assert.That(text, Does.Not.EndWith("\n"));
    }

    [Test]
    public void ExtractionSystemPrompt_WithoutRules_HasNoSection()
    {
        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(Bare);

        Assert.That(prompt, Does.Not.Contain(EditorialBriefPrompt.Heading));
        Assert.That(prompt, Does.Contain("reviewer can judge it at a glance.\n\n## What to Extract"));
    }

    [Test]
    public void ExtractionSystemPrompt_WithRules_PutsTheSectionBetweenThePurposeAndTheRules()
    {
        var request = new ExtractionRequest
        {
            SourceBody = Bare.SourceBody,
            SourceTitle = Bare.SourceTitle,
            SourceType = Bare.SourceType,
            SourceVisibility = Bare.SourceVisibility,
            EditorialRules = ["Shop inventories are background."],
        };

        var prompt = ExtractionPromptBuilder.BuildSystemPrompt(request);

        var purpose = prompt.IndexOf("reviewer can judge it at a glance.", StringComparison.Ordinal);
        var brief = prompt.IndexOf(EditorialBriefPrompt.Heading, StringComparison.Ordinal);
        var rules = prompt.IndexOf("## What to Extract", StringComparison.Ordinal);
        Assert.That(purpose, Is.LessThan(brief));
        Assert.That(brief, Is.LessThan(rules));
        Assert.That(prompt, Does.Contain("1. Shop inventories are background."));
        Assert.That(prompt, Does.Contain("## Standing Instructions\n"), "the heading starts its own line");
    }

    [Test]
    public void CheckInPrompt_CarriesTheBrief_TheCount_AndEveryCandidateByKey()
    {
        var candidates = new List<CheckInCandidate>
        {
            new("c1", CheckInQuestionKind.Storyline, Guid.NewGuid(), "The Warning",
                "Is “The Warning” something the table is pursuing, or background?", "Quiet for 3 sessions.",
                new CheckInEvidenceBlock { Summary = "A witch warned Koves.", SessionsQuiet = 3, RelatedEntries = [new CheckInRelatedEntry(Guid.NewGuid(), "Koves", "Character")] }),
            new("c2", CheckInQuestionKind.Pattern, null, "eye colour", "Keep recording “eye colour”?", "8 facts across 3 entries.",
                new CheckInEvidenceBlock { Predicate = "eye colour", FactCount = 8, ArtifactCount = 3, Examples = ["grey", "green"] }),
        };

        var system = CheckInPrompt.BuildSystemPrompt(4);
        var user = CheckInPrompt.BuildUserMessage("Vespergale", 4, ["Shop inventories are background."], candidates);

        Assert.Multiple(() =>
        {
            Assert.That(system, Does.Contain("4"));
            Assert.That(user, Does.Contain("## World: Vespergale"));
            Assert.That(user, Does.Contain("Ask at most 4 of the 2 candidates"));
            Assert.That(user, Does.Contain(EditorialBriefPrompt.Heading));
            Assert.That(user, Does.Contain("[c1]").And.Contain("[c2]"));
            Assert.That(user, Does.Contain("Koves"));
            Assert.That(user, Does.Contain("eye colour"));
        });
    }
}
