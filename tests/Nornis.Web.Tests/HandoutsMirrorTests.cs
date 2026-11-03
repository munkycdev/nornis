using System.Text.RegularExpressions;
using Nornis.Web.ApiClient;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The Handouts card's picker mirrors the Application's rules, which the Web can't reference
/// (feature 38): the limit and the file types a booklet prints. This reads their source and
/// requires them to agree, so the picker never offers what the API refuses.
/// </summary>
[TestFixture]
public class HandoutsMirrorTests
{
    [Test]
    public void TheLimit_IsAdventureDesignServices()
    {
        var source = Read("src", "Nornis.Application", "Services", "AdventureDesignService.cs");
        var match = Regex.Match(source, @"public const int MaxHandouts\s*=\s*(?<n>\d+);");

        Assert.That(match.Success, Is.True);
        Assert.That(HandoutChoices.MaxHandouts, Is.EqualTo(int.Parse(match.Groups["n"].Value)));
    }

    [Test]
    public void ThePrintableTypes_AreHandoutFiles()
    {
        var source = Read("src", "Nornis.Application", "Prep", "HandoutFiles.cs");
        var types = Regex.Matches(source, "\"(?<t>[a-z]+/[a-z0-9.+-]+)\"").Select(m => m.Groups["t"].Value).ToHashSet();

        Assert.That(types, Is.Not.Empty);
        Assert.That(HandoutChoices.PrintableTypes, Is.EquivalentTo(types));
    }

    [Test]
    public void TheCategories_AreTheApisMapAndHandout()
    {
        var source = Read("src", "Nornis.Application", "Services", "AdventureDesignService.cs");

        Assert.That(source, Does.Contain("LibraryDocumentCategory.Map or LibraryDocumentCategory.Handout"));
        Assert.That(HandoutChoices.Categories, Is.EqualTo([LibraryDocumentDto.MapCategory, LibraryDocumentDto.HandoutCategory]));
    }

    [Test]
    public void Move_SwapsNeighbours_AndLeavesTheEndsAlone()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        IReadOnlyList<Guid> ids = [a, b, c];

        Assert.Multiple(() =>
        {
            Assert.That(HandoutChoices.Move(ids, 1, -1), Is.EqualTo([b, a, c]));
            Assert.That(HandoutChoices.Move(ids, 1, 1), Is.EqualTo([a, c, b]));
            Assert.That(HandoutChoices.Move(ids, 0, -1), Is.EqualTo(ids));
            Assert.That(HandoutChoices.Move(ids, 2, 1), Is.EqualTo(ids));
        });
    }

    private static string Read(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null);
        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }
}
