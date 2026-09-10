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
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Sans },
            H1 = new H1Typography { FontFamily = Serif },
            H2 = new H2Typography { FontFamily = Serif },
            H3 = new H3Typography { FontFamily = Serif },
            H4 = new H4Typography { FontFamily = Serif },
            H5 = new H5Typography { FontFamily = Serif },
            H6 = new H6Typography { FontFamily = Serif },
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "264px",
        },
    };
}
