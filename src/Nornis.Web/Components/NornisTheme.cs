using MudBlazor;

namespace Nornis.Web.Components;

/// <summary>
/// The single source of truth for Nornis colours and type, expressed as a MudBlazor theme.
/// Components and CSS never hard-code colours — CSS reads the generated <c>--mud-palette-*</c>
/// custom properties, and components use MudBlazor <see cref="Color"/> roles. Layout and
/// spacing live in app.css (also theme-oriented, via the same custom properties).
///
/// 2.0 is paper (feature 24, phase C): one warm ground, ink for text, a sidebar that is a tint
/// of the page behind a hairline rather than a block of its own, and a single accent —
/// terracotta — reserved for the primary action and the active navigation icon. Gold survives
/// on the stonemark and the public hero, where it is the brand's colour rather than the
/// interface's. The constants are the table in the feature's design doc; tests pin them.
/// </summary>
public static class NornisTheme
{
    public const string Paper = "#FAF8F3";
    public const string Sidebar = "#F0ECE3";
    public const string Ink = "#1C1F24";
    public const string InkSecondary = "#6B7079";
    public const string Lines = "#E3DED3";
    public const string Accent = "#8B4A3C";
    public const string Gold = "#C4A15A";

    // The same product after dark: ink and paper trade places, the accent lifts two steps so it
    // still clears AA against the dark page, and it takes the page colour as its own text.
    public const string PaperDark = "#151719";
    public const string SidebarDark = "#1B1E22";
    public const string InkDark = "#ECE8E0";
    public const string InkSecondaryDark = "#9A9FA6";
    public const string LinesDark = "#30343A";
    public const string AccentDark = "#C0705E";

    /// <summary>
    /// Feature 42: a muted hue per artifact type, so a person, a place and a plot can be told apart
    /// before the label is read. They mark only a type's icon tile and its small kind label (never
    /// running text, buttons or links: terracotta stays the one accent for action). Each clears 3:1
    /// on the card surface of its palette, the bar for a non-text mark. Document has no hue of its
    /// own and reads in secondary ink, as does any type not listed. app.css mirrors these as
    /// <c>--nornis-type-{type}</c> tokens; components reach them only through
    /// <c>ArtifactTypeDisplay.TypeClass</c>.
    /// </summary>
    public static class TypeColours
    {
        public static readonly IReadOnlyDictionary<string, string> Light = new Dictionary<string, string>
        {
            ["Character"] = "#3F6079",
            ["Location"] = "#557548",
            ["Item"] = "#9C7330",
            ["Faction"] = "#6E4A6B",
            ["Event"] = "#3E7672",
            ["Storyline"] = "#A7843B",
            ["Concept"] = "#5E6080",
        };

        public static readonly IReadOnlyDictionary<string, string> Dark = new Dictionary<string, string>
        {
            ["Character"] = "#7FA3BF",
            ["Location"] = "#8DB07E",
            ["Item"] = "#D2A75C",
            ["Faction"] = "#B48AB0",
            ["Event"] = "#79B5AF",
            ["Storyline"] = "#D6B672",
            ["Concept"] = "#9EA0C2",
        };
    }

    public static readonly string[] Serif = ["Newsreader", "Georgia", "serif"];
    public static readonly string[] Sans = ["IBM Plex Sans", "Segoe UI", "Roboto", "Helvetica", "Arial", "sans-serif"];

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            // The one accent, and the two inks
            Primary = Accent,             // primary action, active nav icon — nothing else
            Secondary = InkSecondary,     // quiet icons and captions; the old gold role, retired
            Tertiary = "#3F6079",         // slate-blue — the view-as-player indicator

            // Ground
            Black = Ink,
            White = "#FFFFFF",
            Background = Paper,
            BackgroundGray = Sidebar,
            Surface = "#FFFFFF",

            // Chrome (the sidebar is a tint of the page, its text one step lighter than ink)
            AppbarBackground = Paper,
            AppbarText = Ink,
            DrawerBackground = Sidebar,
            DrawerText = "#3A3F46",
            DrawerIcon = InkSecondary,

            // Text
            TextPrimary = Ink,
            TextSecondary = InkSecondary,
            TextDisabled = "#A5A9AF",
            ActionDefault = InkSecondary,
            ActionDisabled = "#C2C5CA",

            // Lines
            Divider = Lines,
            DividerLight = "#EDE9E0",
            LinesDefault = Lines,
            LinesInputs = "#D6D1C6",
            TableLines = Lines,

            // Status (muted, per design system)
            Success = "#4E9A6B",
            Warning = "#C08A2E",
            Error = "#B5533F",
            Info = "#3F6079",

            GrayLight = Sidebar,
            GrayLighter = Paper,
        },
        PaletteDark = new PaletteDark
        {
            Primary = AccentDark,
            PrimaryContrastText = PaperDark,
            Secondary = InkSecondaryDark,
            Tertiary = "#7FA3BF",

            Black = PaperDark,
            White = InkDark,
            Background = PaperDark,
            BackgroundGray = SidebarDark,
            Surface = "#1E2126",

            AppbarBackground = PaperDark,
            AppbarText = InkDark,
            DrawerBackground = SidebarDark,
            DrawerText = "#C9C5BD",
            DrawerIcon = InkSecondaryDark,

            TextPrimary = InkDark,
            TextSecondary = InkSecondaryDark,
            TextDisabled = "#6B7079",
            ActionDefault = InkSecondaryDark,
            ActionDisabled = "#4A4F57",

            Divider = LinesDark,
            DividerLight = "#262A2F",
            LinesDefault = LinesDark,
            LinesInputs = "#3C4147",
            TableLines = LinesDark,

            Success = "#6FB98A",
            Warning = "#D9A24A",
            Error = "#D46A57",
            Info = "#7FA3BF",

            GrayLight = SidebarDark,
            GrayLighter = PaperDark,
            Dark = "#1E2126",
            DarkContrastText = InkDark,
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Sans },
            H1 = new H1Typography { FontFamily = Serif },
            H2 = new H2Typography { FontFamily = Serif },
            H3 = new H3Typography { FontFamily = Serif },
            H4 = new H4Typography { FontFamily = Serif },
            H5 = new H5Typography { FontFamily = Serif },
            H6 = new H6Typography { FontFamily = Serif },

            // Buttons and tabs read as words, not as an enterprise console: MudBlazor's default is
            // uppercase with tracking, which fought the serif voice and was overridden in some
            // places and not others. Uppercase stays where it carries meaning — the small eyebrow
            // labels in app.css.
            Button = new ButtonTypography { FontFamily = Sans, TextTransform = "none", LetterSpacing = "normal", FontWeight = "500" },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "264px",
        },
    };
}
