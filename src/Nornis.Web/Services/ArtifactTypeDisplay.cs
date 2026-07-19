using MudBlazor;

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
}
