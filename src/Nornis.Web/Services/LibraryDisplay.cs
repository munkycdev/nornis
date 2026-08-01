using MudBlazor;

namespace Nornis.Web.Services;

/// <summary>
/// Presentation for library document status. The list and the detail page must say the
/// same thing — the detail page used to render a raw "IndexFailed" where the list said
/// "Index failed".
/// </summary>
public static class LibraryDisplay
{
    public static string StatusLabel(string status, int chunkCount) => status switch
    {
        "Indexed" => $"Indexed · {chunkCount} passages",
        "Indexing" => "Indexing…",
        "IndexFailed" => "Index failed",
        "Stored" => "Stored",
        _ => status,
    };

    public static Color StatusColor(string status) => status switch
    {
        "Indexed" => Color.Success,
        "Indexing" => Color.Info,
        "IndexFailed" => Color.Error,
        _ => Color.Default,
    };
}
