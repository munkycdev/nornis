using System.Text.RegularExpressions;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests.Services;

[TestFixture]
public class VisibilityDisplayTests
{
    [TestCase("PartyVisible", "Party visible")]
    [TestCase("GMOnly", "GM only")]
    [TestCase("Private", "Private")]
    public void Label_ReadsAsWords_NotAsTheIdentifier(string scope, string expected)
    {
        Assert.That(VisibilityDisplay.Label(scope), Is.EqualTo(expected));
    }

    [Test]
    public void Label_LeavesAnUnknownScopeAlone()
    {
        Assert.That(VisibilityDisplay.Label("Hidden"), Is.EqualTo("Hidden"));
    }

    [TestCase("PartyVisible", false)]
    [TestCase("GMOnly", true)]
    [TestCase("Private", true)]
    public void IsWorthShowing_IsSilentOnlyForTheRestingScope(string scope, bool expected)
    {
        Assert.That(VisibilityDisplay.IsWorthShowing(scope), Is.EqualTo(expected));
    }

    /// <summary>
    /// The label existed and four pages printed the identifier anyway — "PartyVisible" and
    /// "GMOnly" on Sources, a source and an entry. A scope written straight into markup is the
    /// shape of that mistake, so it is what this looks for.
    /// </summary>
    [Test]
    public void NoPage_PrintsARawScope()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Web")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test binary.");
        var offenders = Directory.EnumerateFiles(Path.Combine(dir!.FullName, "src", "Nornis.Web", "Components"), "*.razor", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @">\s*@[\w.]*\bVisibility\s*<"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.That(offenders, Is.Empty, "render scopes through VisibilityDisplay.Label");
    }

    [Test]
    public void EveryScope_HasALabelThatIsNotTheIdentifier()
    {
        foreach (var scope in VisibilityDisplay.Scopes.Where(s => s != "Private"))
        {
            Assert.That(VisibilityDisplay.Label(scope), Is.Not.EqualTo(scope), scope);
        }
    }
}
