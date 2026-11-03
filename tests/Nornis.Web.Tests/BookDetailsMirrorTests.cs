using System.Text.RegularExpressions;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The Book details card's field limits mirror <c>AdventureDetails</c>' bounds, which the Web
/// can't reference. A limit above the Domain's lets the GM type a value the API refuses; one
/// below it refuses a value the API would take. This reads the Domain source and requires both
/// to agree.
/// </summary>
[TestFixture]
public class BookDetailsMirrorTests
{
    [TestCase("MaxGameSystemLength", BookDetailsCard.MaxGameSystemLength)]
    [TestCase("MaxEditionLength", BookDetailsCard.MaxEditionLength)]
    [TestCase("MaxLevelsLength", BookDetailsCard.MaxLevelsLength)]
    public void TheCardsLimit_IsTheDomainsBound(string name, int mirrored)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Domain", "Models", "AdventureDetails.cs"));
        var match = Regex.Match(source, $@"public const int {name}\s*=\s*(?<n>[\d_]+);");

        Assert.That(match.Success, Is.True, $"no {name} in AdventureDetails.cs");
        Assert.That(mirrored, Is.EqualTo(int.Parse(match.Groups["n"].Value.Replace("_", ""))));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
