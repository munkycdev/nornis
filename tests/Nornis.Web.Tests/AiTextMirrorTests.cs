using System.Text.RegularExpressions;
using Nornis.Web.Services;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Feature 39's names, which the Web mirrors because it can't reference Application: the refusal
/// codes it renders as "add a key" (a renamed code would read as a plain fault), and the provider
/// names an out-of-credit message is matched on for its billing link.
/// </summary>
[TestFixture]
public class AiTextMirrorTests
{
    private static string Source(string relativePath) => File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

    [Test]
    public void TheRefusalCodes_AreTheApplicationsRefusalCodes()
    {
        var codes = Regex.Matches(Source("src/Nornis.Application/Ai/AiRefusalException.cs"), @"""(ai_[a-z_]+)""")
            .Select(m => m.Groups[1].Value).Distinct();

        Assert.That(new[] { AiText.RequiredCode, AiText.RejectedCode, AiText.CreditCode }, Is.EquivalentTo(codes));
    }

    [Test]
    public void TheProviderNames_AreTheApplicationsDisplayNames()
    {
        var names = Regex.Matches(Source("src/Nornis.Application/Ai/AiProviderNames.cs"), @"AiProvider\.(\w+)\s*=>\s*""([^""]+)""")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        Assert.That(names, Is.Not.Empty, "found no display names in AiProviderNames.cs");
        Assert.That(names.ToDictionary(n => n.Key, n => AiText.Provider(n.Key)), Is.EqualTo(names));
    }

    [Test]
    public void TheOutOfCreditMessage_NamesTheProviderTheWayTheWebLooksForIt() =>
        Assert.That(Source("src/Nornis.Application/Ai/AiRefusalException.cs"),
            Does.Contain("$\"The {AiProviderNames.Display(Provider!.Value)} account behind"));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return dir!.FullName;
    }
}
