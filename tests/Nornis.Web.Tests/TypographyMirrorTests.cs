using System.Globalization;
using System.Text.RegularExpressions;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The Typography card's faces and bounds mirror <c>BookFont</c> and <c>BookTypography</c>, which
/// the Web can't reference. A face the card offers that the API doesn't know is refused on save;
/// one the API knows that the card leaves out can never be chosen. This reads the Domain source
/// and requires both to agree.
/// </summary>
[TestFixture]
public class TypographyMirrorTests
{
    [Test]
    public void TheCardsFonts_AreTheDomainsFaces_InOrder_WithTheirNames()
    {
        var enumSource = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Domain", "Enums", "BookFont.cs"));
        var body = enumSource[(enumSource.IndexOf("public enum BookFont", StringComparison.Ordinal))..];
        var members = Regex.Matches(body, @"^\s*(?<name>[A-Z]\w*),\s*$", RegexOptions.Multiline).Select(m => m.Groups["name"].Value).ToList();

        var typeSource = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Domain", "Models", "BookTypography.cs"));
        var names = Regex.Matches(typeSource, @"BookFont\.(?<key>\w+) => ""(?<name>[^""]+)""")
            .ToDictionary(m => m.Groups["key"].Value, m => m.Groups["name"].Value);

        Assert.Multiple(() =>
        {
            Assert.That(TypographyCard.Fonts.Select(f => f.Key), Is.EqualTo(members));
            Assert.That(TypographyCard.Fonts.Select(f => f.Name), Is.EqualTo(members.Select(m => names[m])));
            Assert.That(TypographyCard.DefaultFont, Is.EqualTo(Regex.Match(typeSource, @"DefaultFont = BookFont\.(?<key>\w+);").Groups["key"].Value));
        });
    }

    private static IEnumerable<TestCaseData> Bounds()
    {
        yield return new TestCaseData("DefaultSize", TypographyCard.DefaultSize);
        yield return new TestCaseData("MinSize", TypographyCard.MinSize);
        yield return new TestCaseData("MaxSize", TypographyCard.MaxSize);
        yield return new TestCaseData("SizeStep", TypographyCard.SizeStep);
        yield return new TestCaseData("DefaultLineSpacing", TypographyCard.DefaultLineSpacing);
        yield return new TestCaseData("MinLineSpacing", TypographyCard.MinLineSpacing);
        yield return new TestCaseData("MaxLineSpacing", TypographyCard.MaxLineSpacing);
        yield return new TestCaseData("LineSpacingStep", TypographyCard.LineSpacingStep);
    }

    [TestCaseSource(nameof(Bounds))]
    public void TheCardsBound_IsTheDomainsBound(string name, decimal mirrored)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Domain", "Models", "BookTypography.cs"));
        var match = Regex.Match(source, $@"public const decimal {name}\s*=\s*(?<n>[\d.]+)m;");

        Assert.That(match.Success, Is.True, $"no {name} in BookTypography.cs");
        Assert.That(mirrored, Is.EqualTo(decimal.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture)));
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
