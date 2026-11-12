namespace Nornis.Web.Services;

/// <summary>
/// The single source of truth for capture-type presentation: which types the capture
/// pickers offer (and their order) and the display label for every type, including
/// legacy types retired from capture that existing sources still carry.
/// </summary>
public static class SourceTypeDisplay
{
    /// <summary>Types offered when capturing or editing a source, in display order.</summary>
    public static readonly IReadOnlyList<(string Value, string Label)> CaptureOptions =
    [
        ("SessionNote", "Session note"),
        ("HandwrittenNotes", "Handwritten note"),
        ("ImportedNote", "Imported note"),
        ("GMNote", "GM note"),
        ("FanFiction", "Fan fiction"),
        ("WebLink", "Web link"),
        ("Image", "Image"),
        ("Map", "Map"),
        ("Upload", "Upload"),
        ("SessionAudio", "Session transcript"),
    ];

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["SessionNote"] = "Session note",
        ["HandwrittenNotes"] = "Handwritten note",
        ["ImportedNote"] = "Imported note",
        ["GMNote"] = "GM note",
        ["FanFiction"] = "Fan fiction",
        ["WebLink"] = "Web link",
        ["Image"] = "Image",
        ["Map"] = "Map",
        ["Upload"] = "Upload",
        ["SessionAudio"] = "Session transcript",
        // Legacy types — retired from capture, still displayed on existing sources.
        ["JournalEntry"] = "Journal entry",
        ["Transcript"] = "Transcript",
        // System-generated — never captured, shown on the sources ledger as provenance.
        ["Reveal"] = "Reveal",
        ["LibraryExcerpt"] = "Library excerpt",
        ["AdventureExport"] = "Adventure prep",
    };

    public static string Label(string type) =>
        Labels.TryGetValue(type, out var label) ? label : type;

    /// <summary>
    /// What the one date on a source is called: "Session date" where the source is a session,
    /// "Date" for everything else. Before 3.32 the same field was "When it happened" on Capture,
    /// "Session date" on import and "Occurred" on the source page, and the Timeline's hint asked
    /// for an "occurred at" date no screen offered under that name.
    /// </summary>
    public static string DateLabel(string type) => IsSession(type) ? "Session date" : "Date";

    private static bool IsSession(string type) =>
        type is "SessionNote" or "HandwrittenNotes" or "ImportedNote" or "SessionAudio" or "JournalEntry" or "Transcript";

    /// <summary>
    /// The icon a source wears in a list, by what it is. Every row had the same document icon
    /// until 3.32, so a map, a session and a GM note could only be told apart by reading.
    /// </summary>
    public static string Icon(string type) => type switch
    {
        "SessionNote" or "JournalEntry" or "ImportedNote" => MudBlazor.Icons.Material.Outlined.EditNote,
        "HandwrittenNotes" => MudBlazor.Icons.Material.Outlined.Draw,
        "GMNote" => MudBlazor.Icons.Material.Outlined.StickyNote2,
        "FanFiction" => MudBlazor.Icons.Material.Outlined.AutoStories,
        "WebLink" => MudBlazor.Icons.Material.Outlined.Link,
        "Image" => MudBlazor.Icons.Material.Outlined.Image,
        "Map" => MudBlazor.Icons.Material.Outlined.Map,
        "Upload" => MudBlazor.Icons.Material.Outlined.UploadFile,
        "SessionAudio" or "Transcript" => MudBlazor.Icons.Material.Outlined.GraphicEq,
        "Reveal" => MudBlazor.Icons.Material.Outlined.Visibility,
        "LibraryExcerpt" => MudBlazor.Icons.Material.Outlined.MenuBook,
        "AdventureExport" => MudBlazor.Icons.Material.Outlined.HistoryEdu,
        _ => MudBlazor.Icons.Material.Outlined.Description,
    };

    public static bool IsCaptureOption(string type) =>
        CaptureOptions.Any(o => o.Value == type);

    // Mirrors SourceCapture.IsGmOnly in Domain, which Web does not reference. The API refuses
    // a non-GM's GM note regardless; this only keeps the picker from offering it.
    private static readonly IReadOnlyList<(string Value, string Label)> NonGmCaptureOptions =
        CaptureOptions.Where(o => o.Value != "GMNote").ToList();

    /// <summary>The capture types this member may pick.</summary>
    public static IReadOnlyList<(string Value, string Label)> CaptureOptionsFor(bool isGm) =>
        isGm ? CaptureOptions : NonGmCaptureOptions;

    public static bool IsCaptureOption(string type, bool isGm) =>
        CaptureOptionsFor(isGm).Any(o => o.Value == type);
}
