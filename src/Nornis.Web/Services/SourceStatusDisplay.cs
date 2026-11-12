using MudBlazor;

namespace Nornis.Web.Services;

/// <summary>
/// Presentation for source processing status. Mirrors <see cref="SourceTypeDisplay"/>
/// for types.
/// </summary>
public static class SourceStatusDisplay
{
    /// <summary>
    /// Filter chips in pipeline order, one status per thing a person can tell apart. Ready and
    /// Queued are both "Waiting" (see <see cref="Label"/>), so only one of them is a chip.
    /// </summary>
    public static readonly string[] FilterOrder =
        ["Draft", "Queued", "Processing", "Processed", "Failed"];

    /// <summary>
    /// What a status is called on screen. The pipeline has two waiting states (Ready: saved and
    /// about to be queued; Queued: on the queue) that showed side by side as two chips nobody
    /// could tell apart, and "Processing"/"Processed" beside buttons that said "read" (3.32).
    /// </summary>
    public static string Label(string status) => status switch
    {
        "Ready" or "Queued" => "Waiting",
        "Processing" => "Reading",
        "Processed" => "In the record",
        _ => status,
    };

    /// <summary>Whether a source belongs under a filter chip: same label, not same raw status.</summary>
    public static bool Matches(string filter, string status) => Label(filter) == Label(status);

    // Waiting reads as Info alongside Reading — the pipeline working as intended is
    // not a warning. (One drifted copy showed it amber.)
    public static Color StatusColor(string status) => status switch
    {
        "Processed" => Color.Success,
        "Processing" or "Queued" or "Ready" => Color.Info,
        "Failed" => Color.Error,
        _ => Color.Default,
    };

    /// <summary>
    /// Whether a list row should carry the status chip. Processed is where every source ends up,
    /// and a green chip on every row is the loudest thing on the page while saying nothing.
    /// </summary>
    public static bool IsWorthShowing(string status) => status != "Processed";
}
