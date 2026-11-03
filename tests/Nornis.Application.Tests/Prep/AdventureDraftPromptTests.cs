using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>
/// The draft prompt (feature 29, task C3): it teaches the markup Prep stores, it puts what
/// changes least first so drafts share a cacheable prefix, and it gives the model the
/// section's neighbours and the GM's instruction.
/// </summary>
[TestFixture]
public class AdventureDraftPromptTests
{
    private static Adventure Adventure()
    {
        var adventure = new Adventure { Id = Guid.NewGuid(), Title = "The Silver Key", Pitch = "A heist on the docks." };
        AdventureSection Section(int order, AdventureSectionKind kind, string title, string body) => new()
        {
            Id = Guid.NewGuid(),
            AdventureId = adventure.Id,
            Kind = kind,
            Title = title,
            BodyMarkdown = body,
            SortOrder = order,
        };

        adventure.Sections =
        [
            Section(1, AdventureSectionKind.Overview, "Overview", "Captain Voss hides the key in the harbour vault."),
            Section(2, AdventureSectionKind.Scene, "Scene 1: The Docks", "> Fog rolls in off the water."),
            Section(3, AdventureSectionKind.Scene, "Scene 2: The Vault", AdventureSkeleton.SceneTemplate),
            Section(4, AdventureSectionKind.Scene, "Scene 3: The Chase", "The thieves run for the bridge."),
            Section(5, AdventureSectionKind.QuickReference, "Quick reference", ""),
        ];
        return adventure;
    }

    private static string Build(Adventure adventure, int sortOrder, string? instruction = "Make the guards nervous.") =>
        AdventureDraftPrompt.BuildUserMessage(new AdventureDraftInput(
            new PrepPromptContext("D&D 5e", adventure.Details.Levels, null, "### Artifacts\n- Captain Voss [Hidden — GM-only truth]"), adventure,
            adventure.Sections.Single(s => s.SortOrder == sortOrder).Id, instruction));

    [Test]
    public void TheSystemPrompt_TeachesEveryMarker_AsPrepMarkupSpellsIt()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AdventureDraftPrompt.SystemPrompt, Does.Contain($"> {PrepMarkup.PropMarker}"));
            Assert.That(AdventureDraftPrompt.SystemPrompt, Does.Contain($"> {PrepMarkup.AskMarker}"));
            Assert.That(AdventureDraftPrompt.SystemPrompt, Does.Contain($"\"{PrepMarkup.StatBlockInfo}\""));
            Assert.That(AdventureDraftPrompt.SystemPrompt, Does.Contain("planned, not played"));
        });
    }

    [Test]
    public void TheMessage_RunsFromWhatChangesLeast_ToWhatChangesMost()
    {
        var message = Build(Adventure(), 3);

        var order = new[]
        {
            "## The world", "Captain Voss [Hidden", "## The adventure: The Silver Key", "### Outline",
            "### Overview", "### The section before", "### The section after",
            "## The section to write", "## The GM's instruction", "Make the guards nervous.",
        }.Select(marker => message.IndexOf(marker, StringComparison.Ordinal)).ToList();

        Assert.That(order, Has.None.EqualTo(-1), message);
        Assert.That(order, Is.Ordered, message);
    }

    [Test]
    public void TheOutline_MarksTheSection_AndWhatIsWritten()
    {
        var message = Build(Adventure(), 3);

        Assert.That(message, Does.Contain("3. [Scene] Scene 2: The Vault (not written yet)  <- the section you are writing"));
        Assert.That(message, Does.Contain("2. [Scene] Scene 1: The Docks (written)"));
        Assert.That(message, Does.Contain("5. [QuickReference] Quick reference (not written yet)"));
    }

    [Test]
    public void AnUntouchedScene_IsWritten_FromItsScaffold()
    {
        var message = Build(Adventure(), 3);

        Assert.That(message, Does.Contain("## The section to write: [Scene] Scene 2: The Vault"));
        Assert.That(message, Does.Contain("Its scaffold, to fill in:"));
    }

    [Test]
    public void AWrittenSection_IsRevised_WithItsCurrentText()
    {
        var message = Build(Adventure(), 2, instruction: null);

        Assert.That(message, Does.Contain("## The section to revise: [Scene] Scene 1: The Docks"));
        Assert.That(message, Does.Contain("Its current text, to revise:\n\n> Fog rolls in off the water."));
        Assert.That(message, Does.Contain("Tighten and improve this section"), "no instruction falls back to a revise");
    }

    [Test]
    public void TheOverview_AppearsOnce_AndAnEmptyNeighbourNotAtAll()
    {
        // Scene 1's neighbours are the Overview (already shown above) and Scene 2 (unwritten).
        var message = Build(Adventure(), 2);

        Assert.That(message.Split("Captain Voss hides the key").Length - 1, Is.EqualTo(1));
        Assert.That(message, Does.Not.Contain("### The section before"));
        Assert.That(message, Does.Not.Contain("### The section after"));
    }

    [Test]
    public void ALongNeighbour_IsCut()
    {
        var adventure = Adventure();
        adventure.Sections.Single(s => s.SortOrder == 4).BodyMarkdown = new string('x', AdventureDraftPrompt.MaxNeighbourChars + 500);

        var message = Build(adventure, 3);

        Assert.That(message, Does.Contain(new string('x', AdventureDraftPrompt.MaxNeighbourChars) + "\n[…cut]"));
        Assert.That(message, Does.Not.Contain(new string('x', AdventureDraftPrompt.MaxNeighbourChars + 1)));
    }

    [Test]
    public void ASectionFromAnotherAdventure_IsRefused() =>
        Assert.That(() => AdventureDraftPrompt.BuildUserMessage(
            new AdventureDraftInput(new PrepPromptContext(null, null, null, ""), Adventure(), Guid.NewGuid(), null)), Throws.ArgumentException);

    // ------------------------------------------------------- threads to use (3.1) --

    private static string BuildWithThreads(Adventure adventure, IReadOnlyList<PrepThread> threads) =>
        AdventureDraftPrompt.BuildUserMessage(new AdventureDraftInput(
            new PrepPromptContext("D&D 5e", adventure.Details.Levels, null, "### Artifacts\n- Captain Voss [Hidden — GM-only truth]"), adventure,
            adventure.Sections.Single(s => s.SortOrder == 1).Id, null, threads));

    [Test]
    public void Threads_SitAfterTheWorld_AndBeforeTheAdventure()
    {
        var message = BuildWithThreads(Adventure(), [new PrepThread("Captain Voss", "Voss is the smuggler king.")]);

        var order = new[]
        {
            "Captain Voss [Hidden", "### Threads ready to pay off", "- Captain Voss: Voss is the smuggler king.",
            "## The adventure: The Silver Key",
        }.Select(marker => message.IndexOf(marker, StringComparison.Ordinal)).ToList();

        Assert.That(order, Has.None.EqualTo(-1), message);
        Assert.That(order, Is.Ordered, message);
    }

    [Test]
    public void NoThreads_MeansNoBlock()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BuildWithThreads(Adventure(), []), Does.Not.Contain("Threads ready to pay off"));
            Assert.That(Build(Adventure(), 1), Does.Not.Contain("Threads ready to pay off"));
        });
    }

    [Test]
    public void Threads_AreCappedInNumber_AndEachLineIsCut()
    {
        var threads = Enumerable.Range(1, AdventureDraftPrompt.MaxThreads + 2)
            .Select(i => new PrepThread($"Anchor {i}", new string('y', AdventureDraftPrompt.MaxThreadChars)))
            .ToList();

        var message = BuildWithThreads(Adventure(), threads);
        var lines = message.Split('\n').Where(l => l.StartsWith("- Anchor ", StringComparison.Ordinal)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lines, Has.Count.EqualTo(AdventureDraftPrompt.MaxThreads));
            Assert.That(lines, Has.All.Length.EqualTo("- ".Length + AdventureDraftPrompt.MaxThreadChars + "…".Length));
            Assert.That(message, Does.Not.Contain($"Anchor {AdventureDraftPrompt.MaxThreads + 1}:"));
        });
    }
}
