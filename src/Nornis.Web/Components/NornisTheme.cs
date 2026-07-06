using MudBlazor;

namespace Nornis.Web.Components;

/// <summary>
/// Nornis design tokens expressed as a MudBlazor theme: warm off-white body, cream cards,
/// deep-blue navigation, deep blue-grey text, and a restrained aged-gold accent. Light theme
/// first, per the UI design system.
/// </summary>
public static class NornisTheme
{
    // Core palette (from .kiro/steering/ui-design-system.md).
    public const string BodyBackground = "#F8F5EF";
    public const string CardBackground = "#FFFDF8";
    public const string PrimaryText = "#172A36";
    public const string SidebarNav = "#0F1F2D";
    public const string AgedGold = "#C4A15A";
    public const string Slate = "#6D7A80";

    public static readonly MudTheme Theme = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = SidebarNav,
            Secondary = AgedGold,
            Tertiary = Slate,
            Black = PrimaryText,
            Background = BodyBackground,
            BackgroundGray = BodyBackground,
            Surface = CardBackground,
            AppbarBackground = SidebarNav,
            AppbarText = "#F3EEE4",
            DrawerBackground = SidebarNav,
            DrawerText = "#D9D2C4",
            DrawerIcon = AgedGold,
            TextPrimary = PrimaryText,
            TextSecondary = Slate,
            ActionDefault = Slate,
            Divider = "#E7E0D3",
            DividerLight = "#EFEAE0",
            Success = "#4F7A5B",
            Warning = "#B7891F",
            Error = "#B5533F",
            Info = "#3F6079",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "248px",
        },
    };
}
