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
        ("SessionNote", "Session Note"),
        ("HandwrittenNotes", "Handwritten Note"),
        ("ImportedNote", "Imported Note"),
        ("GMNote", "GM Note"),
        ("FanFiction", "Fan Fiction"),
        ("WebLink", "Web Link"),
        ("Image", "Image"),
        ("Map", "Map"),
        ("Upload", "Upload"),
        ("SessionAudio", "Session Audio"),
    ];

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["SessionNote"] = "Session Note",
        ["HandwrittenNotes"] = "Handwritten Note",
        ["ImportedNote"] = "Imported Note",
        ["GMNote"] = "GM Note",
        ["FanFiction"] = "Fan Fiction",
        ["WebLink"] = "Web Link",
        ["Image"] = "Image",
        ["Map"] = "Map",
        ["Upload"] = "Upload",
        ["SessionAudio"] = "Session Audio",
        // Legacy types — retired from capture, still displayed on existing sources.
        ["JournalEntry"] = "Journal Entry",
        ["Transcript"] = "Transcript",
    };

    public static string Label(string type) =>
        Labels.TryGetValue(type, out var label) ? label : type;

    public static bool IsCaptureOption(string type) =>
        CaptureOptions.Any(o => o.Value == type);
}
