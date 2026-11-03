using Nornis.Application.Prep;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// Feature 35, Requirement 2.3: drafting and the side conversation describe Prep's markup with
/// one text, and the worked example in it is a block the grammar actually reads as intended.
/// </summary>
[TestFixture]
public class PromptConventionsTests
{
    [Test]
    public void BothPrepPrompts_CarryTheConventionsVerbatim()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AdventureDraftPrompt.SystemPrompt, Does.Contain(PrepMarkup.ConventionsForPrompt));
            Assert.That(AdventureConversationPrompt.SystemPrompt, Does.Contain(PrepMarkup.ConventionsForPrompt));
        });
    }

    [Test]
    public void TheConventions_NameEveryMarker()
    {
        Assert.That(PrepMarkup.ConventionsForPrompt, Does.Contain(PrepMarkup.PropMarker)
            .And.Contain(PrepMarkup.AskMarker)
            .And.Contain($"\"{PrepMarkup.StatBlockInfo}\"")
            .And.Contain($"\"{PrepMarkup.ItemBlockInfo}\""));
    }

    [Test]
    public void TheWorkedExample_ParsesAsANamedStatBlockAndANamedItem()
    {
        var blocks = GameBlocks.InMarkdown(PrepMarkup.ConventionsForPrompt);

        Assert.Multiple(() =>
        {
            Assert.That(blocks.Select(b => (b.Kind, b.Name)), Is.EqualTo(((GameBlockKind, string?)[])
            [
                (GameBlockKind.Stat, "Captain Voss"),
                (GameBlockKind.Item, "Cloak of the Tidecaller"),
            ]));
            Assert.That(blocks[0].Subtitle, Is.EqualTo("Medium human, pirate captain"));
            Assert.That(blocks[0].Groups.Select(g => g.Heading), Is.EqualTo((string?[])[null, "Actions"]));
            Assert.That(blocks[1].Description, Is.EqualTo((string[])["While you wear it you can breathe water."]));
            Assert.That(blocks.All(b => GameBlocks.ExportLineFor(b, null) is { WasCut: false }), Is.True);
        });
    }

    [Test]
    public void TheConventions_AskForReuse_AndNoCopiedPublishedBlocks()
    {
        Assert.That(PrepMarkup.ConventionsForPrompt, Does.Contain("\"stat block\" or \"item details\" fact")
            .And.Contain("Don't reproduce a published book's stat block"));
    }
}
