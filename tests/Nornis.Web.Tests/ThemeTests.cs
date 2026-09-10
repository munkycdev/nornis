using System.Text.RegularExpressions;
using Nornis.Web.Components;
using NUnit.Framework;

namespace Nornis.Web.Tests;

/// <summary>
/// Pins the 2.0 palette and type to the table in the feature 24 design doc, and holds the two
/// places the theme is duplicated by hand — app.css's tokens and App.razor's font link — to
/// the same values. The theme file is the single source of truth by its own account; these
/// are what make that claim checkable rather than aspirational.
/// </summary>
[TestFixture]
public class ThemeTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Nornis.Web")))
        {
            dir = dir.Parent;
        }

        Assert.That(dir, Is.Not.Null, "Could not find the repository root from the test binary.");
        return dir!.FullName;
    }

    /// <summary>MudColor renders as #rrggbbaa; the table is written without the alpha.</summary>
    private static string Hex(MudBlazor.Utilities.MudColor c) => c.Value[..7].ToUpperInvariant();

    [Test]
    public void Palette_IsTheDesignTable()
    {
        var p = NornisTheme.Theme.PaletteLight;
        Assert.Multiple(() =>
        {
            Assert.That(Hex(p.Background), Is.EqualTo("#FAF8F3"), "page");
            Assert.That(Hex(p.DrawerBackground), Is.EqualTo("#F0ECE3"), "sidebar tint");
            Assert.That(Hex(p.TextPrimary), Is.EqualTo("#1C1F24"), "ink");
            Assert.That(Hex(p.TextSecondary), Is.EqualTo("#6B7079"), "secondary");
            Assert.That(Hex(p.Divider), Is.EqualTo("#E3DED3"), "lines");
            Assert.That(Hex(p.Primary), Is.EqualTo("#8B4A3C"), "the one accent");
            Assert.That(Hex(p.Secondary), Is.EqualTo(Hex(p.TextSecondary)),
                "the old gold role is retired: Color.Secondary is quiet ink");
        });
    }

    [Test]
    public void Type_IsNewsreaderAndPlex()
    {
        var t = NornisTheme.Theme.Typography;
        Assert.Multiple(() =>
        {
            Assert.That(t.Default.FontFamily![0], Is.EqualTo("IBM Plex Sans"));
            Assert.That(t.H1.FontFamily![0], Is.EqualTo("Newsreader"));
            Assert.That(t.H6.FontFamily![0], Is.EqualTo("Newsreader"));
            Assert.That(NornisTheme.Theme.LayoutProperties.DefaultBorderRadius, Is.EqualTo("6px"));
        });
    }

    [Test]
    public void StylesheetTokens_MirrorTheTheme()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Web", "wwwroot", "app.css"));
        Assert.Multiple(() =>
        {
            Assert.That(css, Does.Contain("--nornis-serif: 'Newsreader'"));
            Assert.That(css, Does.Contain("--nornis-sans: 'IBM Plex Sans'"));
            Assert.That(css, Does.Not.Contain("Cormorant"));
            Assert.That(css, Does.Not.Contain("'Inter'"));
            Assert.That(css, Does.Not.Contain("onnavy"), "the sidebar is no longer navy; nothing should be styled as if it were");
        });
    }

    [Test]
    public void FontLink_LoadsExactlyTheTwoFamilies()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Web", "Components", "App.razor"));
        var links = Regex.Matches(app, @"fonts\.googleapis\.com/css2\?([^""]+)").Select(m => m.Groups[1].Value).ToList();
        Assert.That(links, Has.Count.EqualTo(1), "one font stylesheet");

        var families = Regex.Matches(links[0], @"family=([A-Za-z+]+)").Select(m => m.Groups[1].Value.Replace('+', ' ')).ToList();
        Assert.That(families, Is.EquivalentTo(["IBM Plex Sans", "Newsreader"]));
    }
}
