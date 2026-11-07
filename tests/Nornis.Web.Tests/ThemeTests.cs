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
    public void Buttons_AreSentenceCase()
    {
        var b = NornisTheme.Theme.Typography.Button;
        Assert.Multiple(() =>
        {
            Assert.That(b.TextTransform, Is.EqualTo("none"), "MudBlazor's default is uppercase");
            Assert.That(b.FontFamily![0], Is.EqualTo("IBM Plex Sans"));
        });
    }

    /// <summary>
    /// 2.0 retired the gold Secondary role to quiet ink, so a filled Secondary button is a slate
    /// block that reads as disabled beside an outlined terracotta one. Forty-nine survived the
    /// palette change because nothing could notice them. A filled button is the page's or the
    /// dialog's main action, and the main action is the one accent.
    /// </summary>
    [Test]
    public void FilledButtons_UseTheAccent()
    {
        var razor = Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Nornis.Web", "Components"), "*.razor", SearchOption.AllDirectories);
        var offenders = razor
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"<MudButton\b[^>]*>", RegexOptions.Singleline)
                .Where(m => m.Value.Contains("Variant.Filled") && m.Value.Contains("Color.Secondary"))
                .Select(_ => Path.GetFileName(file)))
            .ToList();

        Assert.That(offenders, Is.Empty, "filled Secondary buttons render grey; use Color.Primary");
    }

    /// <summary>Feature 42's type colour table, light then dark.</summary>
    private static readonly (string Type, string Light, string Dark)[] TypeTable =
    [
        ("Character", "#3F6079", "#7FA3BF"),
        ("Location", "#557548", "#8DB07E"),
        ("Item", "#9C7330", "#D2A75C"),
        ("Faction", "#6E4A6B", "#B48AB0"),
        ("Event", "#3E7672", "#79B5AF"),
        ("Storyline", "#A7843B", "#D6B672"),
        ("Concept", "#5E6080", "#9EA0C2"),
    ];

    [Test]
    public void TypeColours_AreTheDesignTable()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NornisTheme.TypeColours.Light, Is.EquivalentTo(TypeTable.ToDictionary(t => t.Type, t => t.Light)));
            Assert.That(NornisTheme.TypeColours.Dark, Is.EquivalentTo(TypeTable.ToDictionary(t => t.Type, t => t.Dark)));
            Assert.That(NornisTheme.TypeColours.Light.ContainsKey("Document"), Is.False, "Document reads in secondary ink");
        });
    }

    /// <summary>Requirement 4.3: a type colour is a non-text mark, so 3:1 on the card it sits on.</summary>
    [Test]
    public void EveryTypeColour_ClearsThreeToOne_OnTheCard()
    {
        Assert.Multiple(() =>
        {
            foreach (var (type, hex) in NornisTheme.TypeColours.Light)
            {
                Assert.That(Contrast(new MudBlazor.Utilities.MudColor(hex), NornisTheme.Theme.PaletteLight.Surface),
                    Is.GreaterThanOrEqualTo(3.0), $"{type} on the light card");
            }

            foreach (var (type, hex) in NornisTheme.TypeColours.Dark)
            {
                Assert.That(Contrast(new MudBlazor.Utilities.MudColor(hex), NornisTheme.Theme.PaletteDark.Surface),
                    Is.GreaterThanOrEqualTo(3.0), $"{type} on the dark card");
            }
        });
    }

    /// <summary>The stylesheet's light tokens sit in :root, the dark ones in .nornis-dark.</summary>
    [Test]
    public void StylesheetTypeTokens_MirrorTheTheme()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Nornis.Web", "wwwroot", "app.css"));

        static Dictionary<string, string> Tokens(string css, string selector)
        {
            var block = Regex.Match(css, Regex.Escape(selector) + @"\s*\{([^}]*)\}").Groups[1].Value;
            return Regex.Matches(block, @"--nornis-type-([a-z]+):\s*(#[0-9A-Fa-f]{6});")
                .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.ToUpperInvariant());
        }

        Assert.Multiple(() =>
        {
            Assert.That(Tokens(css, ":root"), Is.EquivalentTo(
                NornisTheme.TypeColours.Light.ToDictionary(t => t.Key.ToLowerInvariant(), t => t.Value)));
            Assert.That(Tokens(css, ".nornis-dark"), Is.EquivalentTo(
                NornisTheme.TypeColours.Dark.ToDictionary(t => t.Key.ToLowerInvariant(), t => t.Value)));
        });
    }

    /// <summary>
    /// Correctness property 5: no component sets a type colour by value or reaches for its token,
    /// and neither does the graph's script; they go through <c>ArtifactTypeDisplay.TypeClass</c>
    /// (the script reads the tokens off the page), so the table is the one place a hue lives. The
    /// graph had a brighter palette of its own before feature 42. Character's slate is also the
    /// palette's Info role, which status marks use in their own right, so a hue that is a palette
    /// colour is not counted.
    /// </summary>
    [Test]
    public void NoComponentOrScript_NamesATypeColour()
    {
        var palette = new[] { NornisTheme.Theme.PaletteLight.Info, NornisTheme.Theme.PaletteDark.Info }.Select(Hex).ToHashSet();
        var hexes = NornisTheme.TypeColours.Light.Values.Concat(NornisTheme.TypeColours.Dark.Values)
            .Where(h => !palette.Contains(h))
            .ToList();
        var web = Path.Combine(RepoRoot(), "src", "Nornis.Web");
        var razor = Directory.EnumerateFiles(Path.Combine(web, "Components"), "*.razor", SearchOption.AllDirectories).ToList();
        var scripts = Directory.EnumerateFiles(Path.Combine(web, "wwwroot", "js"), "*.js", SearchOption.AllDirectories).ToList();

        var offenders = razor.Where(f => File.ReadAllText(f).Contains("--nornis-type-", StringComparison.Ordinal))
            .Concat(razor.Concat(scripts).Where(f => hexes.Any(h => File.ReadAllText(f).Contains(h, StringComparison.OrdinalIgnoreCase))))
            .Select(Path.GetFileName)
            .Distinct()
            .ToList();

        Assert.That(offenders, Is.Empty);
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
