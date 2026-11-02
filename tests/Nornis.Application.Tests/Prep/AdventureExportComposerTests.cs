using Nornis.Application.Prep;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using NUnit.Framework;

namespace Nornis.Application.Tests.Prep;

/// <summary>An export's body (feature 29, Requirement 8.1): the filing line, the structure, and which sections a chapter export takes.</summary>
[TestFixture]
public class AdventureExportComposerTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private static Adventure Adventure(params (string Title, AdventureSectionKind Kind, string Body)[] sections)
    {
        var id = Guid.NewGuid();
        return new Adventure
        {
            Id = id,
            Title = "Everybody Dies at the Slurping Spa",
            Sections = sections.Select((s, i) => new AdventureSection
            {
                Id = Guid.NewGuid(), AdventureId = id, Title = s.Title, Kind = s.Kind, BodyMarkdown = s.Body, SortOrder = i + 1,
            }).ToList(),
        };
    }

    private static readonly (string, AdventureSectionKind, string)[] TwoChapters =
    [
        ("Overview", AdventureSectionKind.Overview, "A spa that eats its guests."),
        ("Chapter 1: Arrival", AdventureSectionKind.Chapter, ""),
        ("Scene 1", AdventureSectionKind.Scene, "The lobby smells of mint."),
        ("Scene 2", AdventureSectionKind.Scene, AdventureSkeleton.SceneTemplate),
        ("Chapter 2: The Baths", AdventureSectionKind.Chapter, "Everything is wet."),
        ("Scene 3", AdventureSectionKind.Scene, "The slime rises."),
    ];

    [Test]
    public void TheBody_StartsWithTheFilingLine_ThenChaptersAsHashAndSectionsAsDoubleHash()
    {
        var adventure = Adventure(TwoChapters);
        var sections = AdventureExportComposer.Select(adventure, null)!;

        var body = AdventureExportComposer.BuildBody(adventure.Title, null, sections, At);

        Assert.That(body, Is.EqualTo(
            "Adventure \"Everybody Dies at the Slurping Spa\", exported 2026-11-14 by the GM as prep\n\n"
            + "## Overview\n\nA spa that eats its guests.\n\n"
            + "# Chapter 1: Arrival\n\n"
            + "## Scene 1\n\nThe lobby smells of mint.\n\n"
            + "# Chapter 2: The Baths\n\nEverything is wet.\n\n"
            + "## Scene 3\n\nThe slime rises."));
    }

    [Test]
    public void AChapterExport_TakesTheChapterAndWhatFollows_UpToTheNextChapter()
    {
        var adventure = Adventure(TwoChapters);
        var chapter1 = adventure.Sections.First(s => s.Title == "Chapter 1: Arrival");

        var sections = AdventureExportComposer.Select(adventure, chapter1.Id)!;

        Assert.That(sections.Select(s => s.Title), Is.EqualTo(new[] { "Chapter 1: Arrival", "Scene 1", "Scene 2" }));
        Assert.That(AdventureExportComposer.BuildFilingLine(adventure.Title, chapter1.Title, At),
            Is.EqualTo("Adventure \"Everybody Dies at the Slurping Spa\", chapter \"Chapter 1: Arrival\", exported 2026-11-14 by the GM as prep"));
    }

    [Test]
    public void TheLastChapter_RunsToTheEnd()
    {
        var adventure = Adventure(TwoChapters);
        var chapter2 = adventure.Sections.First(s => s.Title == "Chapter 2: The Baths");

        Assert.That(AdventureExportComposer.Select(adventure, chapter2.Id)!.Select(s => s.Title),
            Is.EqualTo(new[] { "Chapter 2: The Baths", "Scene 3" }));
    }

    [Test]
    public void ASectionThatIsNotAChapter_SelectsNothing()
    {
        var adventure = Adventure(TwoChapters);

        Assert.That(AdventureExportComposer.Select(adventure, adventure.Sections.First(s => s.Title == "Scene 1").Id), Is.Null);
        Assert.That(AdventureExportComposer.Select(adventure, Guid.NewGuid()), Is.Null);
    }

    [Test]
    public void TheFilingLine_StaysWithinItsAllowance_ForTheLongestTitles()
    {
        var line = AdventureExportComposer.BuildFilingLine(new string('x', 200), new string('y', 200), At);

        Assert.That(line.Length, Is.LessThanOrEqualTo(AdventureSize.ExportFilingLineAllowance));
    }

    [Test]
    public void TheTitle_FitsASourceTitle()
    {
        Assert.That(AdventureExportComposer.BuildTitle("The Silver Key", null), Is.EqualTo("The Silver Key (prep)"));
        Assert.That(AdventureExportComposer.BuildTitle("The Silver Key", "Chapter 2"), Is.EqualTo("The Silver Key: Chapter 2 (prep)"));
        Assert.That(AdventureExportComposer.BuildTitle(new string('x', 200), new string('y', 200)), Has.Length.LessThanOrEqualTo(200));
    }

    [Test]
    public void OneSectionsMarkup_StaysWithinThePerSectionAllowance()
    {
        var adventure = Adventure(("T", AdventureSectionKind.Notes, "B"));
        var body = AdventureExportComposer.BuildBody("A", null, adventure.Sections.ToList(), At);
        var filingLine = AdventureExportComposer.BuildFilingLine("A", null, At);

        var overhead = body.Length - filingLine.Length - "T".Length - "B".Length;

        Assert.That(overhead, Is.LessThanOrEqualTo(AdventureSize.ExportOverheadPerSection));
    }
}
