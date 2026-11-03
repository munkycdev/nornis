using System.Text.RegularExpressions;
using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The Web offers section kinds by name, and the API refuses a name it doesn't know with 400.
/// The names live in Domain, which the Web can't reference, so this reads the enum's source
/// and requires the Web's list to be exactly that list.
/// </summary>
[TestFixture]
public class PrepDisplayTests
{
    [Test]
    public void TheKindsOffered_AreExactlyTheDomainsKinds()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Domain", "Enums", "AdventureSectionKind.cs"));
        var body = source[(source.IndexOf("enum AdventureSectionKind", StringComparison.Ordinal))..];
        var declared = Regex.Matches(body, @"^\s*(\w+),\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();

        Assert.That(declared, Is.Not.Empty, "found no members in AdventureSectionKind.cs");
        Assert.That(PrepDisplay.Kinds.Select(k => k.Kind), Is.EqualTo(declared));
    }

    [TestCase(0, 0, "No sections")]
    [TestCase(1, 1, "1 of 1 section written")]
    [TestCase(4, 7, "4 of 7 sections written")]
    public void Progress_CountsWrittenSections(int filled, int total, string expected) =>
        Assert.That(PrepDisplay.Progress(filled, total), Is.EqualTo(expected));

    [TestCase(false, false, "All,Neither")]
    [TestCase(true, false, "All,Exported")]
    [TestCase(false, true, "All,Published")]
    [TestCase(true, true, "All,Exported,Published")]
    public void TheFilters_PlaceAnAdventure_ByWhatItsBeenThrough(bool exported, bool printed, string expected)
    {
        var adventure = new AdventureSummaryDto(Guid.NewGuid(), "The Silver Key", null, null, 3, 1,
            printed ? Guid.NewGuid() : null, DateTimeOffset.UtcNow, exported);

        var matching = PrepDisplay.Filters.Where(f => PrepDisplay.Matches(f, adventure));

        Assert.That(string.Join(",", matching), Is.EqualTo(expected));
    }

    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid(), D = Guid.NewGuid();

    [Test]
    public void MoveTo_DownTheList_SlidesTheOthersUp() =>
        Assert.That(PrepDisplay.MoveTo([A, B, C, D], A, C), Is.EqualTo([B, C, A, D]));

    [Test]
    public void MoveTo_UpTheList_SlidesTheOthersDown() =>
        Assert.That(PrepDisplay.MoveTo([A, B, C, D], D, B), Is.EqualTo([A, D, B, C]));

    [Test]
    public void MoveTo_AnUnknownId_ChangesNothing() =>
        Assert.That(PrepDisplay.MoveTo([A, B], A, Guid.NewGuid()), Is.EqualTo([A, B]));

    [Test]
    public void MoveBy_StepsOne_AndStopsAtTheEnds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PrepDisplay.MoveBy([A, B, C], B, -1), Is.EqualTo([B, A, C]));
            Assert.That(PrepDisplay.MoveBy([A, B, C], B, 1), Is.EqualTo([A, C, B]));
            Assert.That(PrepDisplay.MoveBy([A, B, C], A, -1), Is.EqualTo([A, B, C]));
            Assert.That(PrepDisplay.MoveBy([A, B, C], C, 1), Is.EqualTo([A, B, C]));
        });
    }

    [Test]
    public void AnUnknownKind_ShowsItsOwnName() =>
        Assert.That(PrepDisplay.Label("Epilogue"), Is.EqualTo("Epilogue"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
