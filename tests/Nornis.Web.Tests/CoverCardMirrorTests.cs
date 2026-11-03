using System.Text.RegularExpressions;
using Nornis.Web.Components.Shared;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// The Cover card's limits and layout names mirror the Domain's and the Application's, which the
/// Web can't reference (feature 38). This reads their source and requires them to agree.
/// </summary>
[TestFixture]
public class CoverCardMirrorTests
{
    [Test]
    public void TheBriefLimit_IsTheDomains()
    {
        var source = Read("src", "Nornis.Domain", "Models", "AdventureCover.cs");
        var match = Regex.Match(source, @"public const int MaxBriefChars\s*=\s*(?<n>[\d_]+);");

        Assert.That(match.Success, Is.True);
        Assert.That(CoverCard.MaxBriefChars, Is.EqualTo(int.Parse(match.Groups["n"].Value.Replace("_", ""))));
    }

    [Test]
    public void TheUploadLimit_IsPrepOptionsDefault()
    {
        var source = Read("src", "Nornis.Application", "Configuration", "PrepOptions.cs");

        Assert.That(source, Does.Contain("public int MaxCoverUploadBytes { get; set; } = 10 * 1024 * 1024;"));
        Assert.That(CoverCard.MaxUploadBytes, Is.EqualTo(10 * 1024 * 1024));
    }

    [Test]
    public void TheLayouts_AreCoverLayoutsValues()
    {
        var source = Read("src", "Nornis.Domain", "Enums", "CoverLayout.cs");
        var values = Regex.Matches(source, @"^\s*(?<v>[A-Z]\w*),\s*$", RegexOptions.Multiline).Select(m => m.Groups["v"].Value);

        Assert.That(CoverCard.Layouts.Select(l => l.Value), Is.EqualTo(values));
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
