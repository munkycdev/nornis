using System.Text.RegularExpressions;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Design mode's bounds (feature 37) as the Web mirrors them, read against the server's source,
/// which the Web can't reference. A Web bound above the server's lets the GM type what the API
/// refuses.
/// </summary>
[TestFixture]
public class DesignModeMirrorTests
{
    [TestCase("Nornis.Domain/Entities/Adventure.cs", "MaxDraftChars", AdventureDesignPanel.MaxDraftChars)]
    [TestCase("Nornis.Domain/Entities/Adventure.cs", "MaxTableBriefChars", TableBriefCard.MaxChars)]
    [TestCase("Nornis.Application/Services/AdventureDesignService.cs", "MaxSourceBooks", SourceBooksCard.MaxBooks)]
    public void TheWebsBound_IsTheServers(string file, string name, int mirrored)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", file));
        var match = Regex.Match(source, $@"public const int {name}\s*=\s*(?<n>[\d_]+);");

        Assert.That(match.Success, Is.True, $"no {name} in {file}");
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
