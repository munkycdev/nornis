using MudBlazor;

namespace Nornis.Web.Services;

/// <summary>
/// Presentation for source processing status. Mirrors <see cref="SourceTypeDisplay"/>
/// for types.
/// </summary>
public static class SourceStatusDisplay
{
    /// <summary>Filter chips in pipeline order.</summary>
    public static readonly string[] FilterOrder =
        ["Draft", "Ready", "Queued", "Processing", "Processed", "Failed"];

    // Queued reads as Info alongside Processing — the pipeline working as intended is
    // not a warning. (One drifted copy showed it amber.)
    public static Color StatusColor(string status) => status switch
    {
        "Processed" => Color.Success,
        "Processing" or "Queued" => Color.Info,
        "Failed" => Color.Error,
        "Ready" => Color.Warning,
        _ => Color.Default,
    };
}
