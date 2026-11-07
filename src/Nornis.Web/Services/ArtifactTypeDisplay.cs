using MudBlazor;
using Nornis.Web.Components;

namespace Nornis.Web.Services;

/// <summary>
/// Presentation for artifact types — currently the icon each type carries in lists and
/// search results. Mirrors <see cref="SourceTypeDisplay"/> for sources.
/// </summary>
public static class ArtifactTypeDisplay
{
    public static string Icon(string? type) => type switch
    {
        "Character" => Icons.Material.Outlined.Person,
        "Location" => Icons.Material.Outlined.Place,
        "Item" => Icons.Material.Outlined.Diamond,
        "Faction" => Icons.Material.Outlined.Groups,
        "Event" => Icons.Material.Outlined.Event,
        "Storyline" => Icons.Material.Outlined.Hub,
        "Concept" => Icons.Material.Outlined.Lightbulb,
        _ => Icons.Material.Outlined.Description,
    };

    /// <summary>
    /// The CSS class that gives an element its type's colour (feature 42), as the
    /// <c>--nornis-type</c> custom property that <c>.nornis-type-tile</c> and
    /// <c>.nornis-type-label</c> read. A type with no hue of its own (Document, or one this
    /// build doesn't know) gets <c>nornis-type-other</c>, which is secondary ink. Components never
    /// name a type colour; they ask for this class.
    /// </summary>
    public static string TypeClass(string? type) =>
        type is not null && NornisTheme.TypeColours.Light.ContainsKey(type)
            ? $"nornis-type-{type.ToLowerInvariant()}"
            : "nornis-type-other";

    /// <summary>
    /// The type as a group heading. Every <c>ArtifactType</c> pluralizes with a bare "s";
    /// this exists so the next one that does not has somewhere to be handled.
    /// </summary>
    public static string Plural(string? type) => $"{type ?? "Other"}s";

    /// <summary>Artifact status chips: Active green, Dormant amber, Resolved blue.</summary>
    public static Color StatusColor(string status) => status switch
    {
        "Active" => Color.Success,
        "Dormant" => Color.Warning,
        "Resolved" => Color.Info,
        _ => Color.Default,
    };

    /// <summary>
    /// Whether a card should carry the status chip. Active is the resting state of an entry; the
    /// chip earns its place when an entry has gone dormant, resolved or been archived.
    /// </summary>
    public static bool IsWorthShowing(string status) => status != "Active";
}
