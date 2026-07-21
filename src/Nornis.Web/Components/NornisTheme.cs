using MudBlazor;

namespace Nornis.Web.Components;

/// <summary>
/// The single source of truth for Nornis colors, expressed as a MudBlazor light-variant theme.
/// Components and CSS never hard-code colors — CSS reads the generated <c>--mud-palette-*</c>
/// custom properties, and components use MudBlazor <see cref="Color"/> roles. Layout, spacing,
/// and typography live in app.css (also theme-oriented, via the same custom properties).
/// </summary>
public static class NornisTheme
{
    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            // Brand
            Primary = "#0E2E4A",          // deep navy — sidebar, primary actions (matches the logo background)
            Secondary = "#C4A15A",        // aged gold — accents, badges, active nav
            Tertiary = "#5E6B7A",         // slate — muted detail

            // Neutrals
            Black = "#0E2E4A",
            White = "#FFFDF9",
            Background = "#F5F1E9",       // warm off-white body
            BackgroundGray = "#EFEADF",
            Surface = "#FFFDF9",          // cream cards

            // Navigation (deep navy sidebar; icons one step brighter than labels,
            // gold reserved for the active item and badges so it stays an accent)
            AppbarBackground = "#FFFDF9",
            AppbarText = "#0E2E4A",
            DrawerBackground = "#0E2E4A",
            DrawerText = "#B7C1CE",
            DrawerIcon = "#D7DEE6",

            // Text
            TextPrimary = "#16293B",
            TextSecondary = "#5E6B7A",
            TextDisabled = "#9AA6B0",
            ActionDefault = "#5E6B7A",
            ActionDisabled = "#B7BFC7",

            // Lines
            Divider = "#E7E0D3",
            DividerLight = "#EFEAE0",
            LinesDefault = "#E7E0D3",
            LinesInputs = "#DBD3C4",
            TableLines = "#E7E0D3",

            // Status (muted, per design system)
            Success = "#4E9A6B",
            Warning = "#C08A2E",
            Error = "#B5533F",
            Info = "#3F6079",

            GrayLight = "#EFEADF",
            GrayLighter = "#F5F1E9",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "12px",
            DrawerWidthLeft = "264px",
        },
    };
}
