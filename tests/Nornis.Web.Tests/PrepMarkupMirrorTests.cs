using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WebPrepMarkup = Nornis.Web.Services.PrepMarkup;

namespace Nornis.Web.Tests;

/// <summary>
/// The Web's <see cref="WebPrepMarkup"/> mirrors Application's <c>PrepMarkup</c>, across a
/// deploy boundary no compiler spans. This reads the Application source and requires every
/// constant there to exist here with the same value, so the editor toolbar can't write a
/// marker the importer and the print renderer don't recognise.
/// </summary>
[TestFixture]
public class PrepMarkupMirrorTests
{
    [Test]
    public void EveryApplicationConstant_HasTheSameValueInTheWebMirror()
    {
        var source = File.ReadAllText(ApplicationPrepMarkupPath());
        var declared = Regex.Matches(source, @"public const string (\w+) = ""([^""]*)"";")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);

        var mirrored = typeof(WebPrepMarkup)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        Assert.That(declared, Is.Not.Empty, "found no constants in Application's PrepMarkup.cs");
        Assert.That(mirrored, Is.EquivalentTo(declared));
    }

    private static string ApplicationPrepMarkupPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Nornis.sln")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "could not locate the repository root from the test output directory");
        return Path.Combine(dir!.FullName, "src", "Nornis.Application", "Prep", "PrepMarkup.cs");
    }
}
