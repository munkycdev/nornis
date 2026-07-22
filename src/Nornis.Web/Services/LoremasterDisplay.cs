using System.Text.RegularExpressions;
using MudBlazor;

namespace Nornis.Web.Services;

/// <summary>
/// Shared presentation rules for Loremaster answers — used by the members' Ask page and
/// the public world's single-shot Ask so the two surfaces can't drift apart.
/// </summary>
public static partial class LoremasterDisplay
{
    /// <summary>Strips inline [ref:…] markers the model emits; citations render separately.</summary>
    public static string CleanAnswer(string text) => RefMarker().Replace(text, "").Trim();

    public static Color ConfidenceColor(string confidence) => confidence switch
    {
        "High" => Color.Success,
        "Medium" => Color.Info,
        "Low" => Color.Warning,
        _ => Color.Default,
    };

    [GeneratedRegex(@"\s*\[ref:[^\]]+\]")]
    private static partial Regex RefMarker();
}
