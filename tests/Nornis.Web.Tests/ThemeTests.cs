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

    private static double Luminance(MudBlazor.Utilities.MudColor c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>WCAG 2 contrast ratio, 1:1 to 21:1.</summary>
    private static double Contrast(MudBlazor.Utilities.MudColor a, MudBlazor.Utilities.MudColor b)
    {
        var l1 = Luminance(a);
        var l2 = Luminance(b);
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    private static IEnumerable<TestCaseData> Palettes()
    {
        yield return new TestCaseData(NornisTheme.Theme.PaletteLight).SetArgDisplayNames("light");
        yield return new TestCaseData(NornisTheme.Theme.PaletteDark).SetArgDisplayNames("dark");
    }

    /// <summary>
    /// Requirement 6.3: AA for body text in both themes. The pairs are the ones a page is made
    /// of — text on the page, text on a card, the sidebar's labels on its tint, the primary
    /// button's label on the button — and the accent as a non-text mark on the page (3:1).
    /// </summary>
    [TestCaseSource(nameof(Palettes))]
    public void EveryTextPair_MeetsAA(MudBlazor.Palette p)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Contrast(p.TextPrimary, p.Background), Is.GreaterThanOrEqualTo(4.5), "ink on page");
            Assert.That(Contrast(p.TextSecondary, p.Background), Is.GreaterThanOrEqualTo(4.5), "secondary on page");
            Assert.That(Contrast(p.TextPrimary, p.Surface), Is.GreaterThanOrEqualTo(4.5), "ink on card");
            Assert.That(Contrast(p.TextSecondary, p.Surface), Is.GreaterThanOrEqualTo(4.5), "secondary on card");
            Assert.That(Contrast(p.DrawerText, p.DrawerBackground), Is.GreaterThanOrEqualTo(4.5), "sidebar label on tint");
            Assert.That(Contrast(p.PrimaryContrastText, p.Primary), Is.GreaterThanOrEqualTo(4.5), "button label on accent");
            Assert.That(Contrast(p.Primary, p.Background), Is.GreaterThanOrEqualTo(3.0), "accent icon on page");
        });
    }

    [Test]
    public void DarkPalette_IsTheDesignTable()
    {
        var p = NornisTheme.Theme.PaletteDark;
        Assert.Multiple(() =>
        {
            Assert.That(Hex(p.Background), Is.EqualTo("#151719"), "page");
            Assert.That(Hex(p.DrawerBackground), Is.EqualTo("#1B1E22"), "sidebar");
            Assert.That(Hex(p.TextPrimary), Is.EqualTo("#ECE8E0"), "ink");
            Assert.That(Hex(p.TextSecondary), Is.EqualTo("#9A9FA6"), "secondary");
            Assert.That(Hex(p.Secondary), Is.EqualTo(Hex(p.TextSecondary)), "no gold role after dark either");
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
