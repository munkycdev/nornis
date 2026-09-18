using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Nornis.Web.Services;

/// <summary>
/// The capture page's form, kept beside its notes: the fields around the text, so a mis-click
/// on the sidebar does not cost a title, a date and a campaign along with the words. Not a
/// Draft — a Draft is a saved <c>Source</c> that has not been sent for extraction; this is the
/// one that was never saved at all.
/// </summary>
public sealed record CaptureFormBackup(
    string Title,
    string Type,
    string Visibility,
    string? Uri,
    DateTime? OccurredAt,
    Guid? CampaignId,
    bool ExtractionEnabled,
    DateTimeOffset SavedAt);

/// <summary>
/// The one place the safety net for unsaved notes is named. Every page that hands a
/// <c>NotesEditor</c> a backup key gets it from here, and every "you have unsaved changes"
/// notice reads the age the same way. The notes themselves are kept by nornis-editor.js in the
/// browser's localStorage; these are the keys it is told to use and the two calls a page makes
/// around an editor it has not mounted yet.
///
/// Three pages keep notes: capture (a note that was never saved), a source's body being edited,
/// and a character's sheet being edited. The second was the one that shipped without it, and
/// the one where a session's notes were lost.
/// </summary>
public static class NotesBackup
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>The capture page's notes for a world. One per world, per browser.</summary>
    public static string CaptureNotesKey(Guid worldId) => $"nornis:capture:{worldId}:notes";

    /// <summary>The capture page's form for a world.</summary>
    public static string CaptureFormKey(Guid worldId) => $"nornis:capture:{worldId}:form";

    /// <summary>A source's body mid-edit.</summary>
    public static string SourceEditKey(Guid sourceId) => $"nornis:source:{sourceId}:edit";

    /// <summary>A character's sheet mid-edit.</summary>
    public static string CharacterSheetKey(Guid characterId) => $"nornis:character:{characterId}:sheet";

    public static string Serialize(CaptureFormBackup form) => JsonSerializer.Serialize(form, Options);

    /// <summary>Null for anything that is not a form this version wrote — garbage is not restored.</summary>
    public static CaptureFormBackup? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<CaptureFormBackup>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// When notes were kept under a key, before any editor is on screen — so a page can say
    /// "you have unsaved changes here" next to its Edit button. Null when nothing is kept, and
    /// null when the browser refuses the question: no storage is the same as nothing kept.
    /// </summary>
    public static async Task<DateTimeOffset?> PeekAsync(IJSRuntime js, string key)
    {
        try
        {
            var at = await js.InvokeAsync<string?>("nornisEditor.peekBackup", key);
            return DateTimeOffset.TryParse(at, null, DateTimeStyles.RoundtripKind, out var keptAt) ? keptAt : null;
        }
        catch (JSException)
        {
            return null;
        }
    }

    /// <summary>Forgets what was kept under a key, whether or not an editor is on screen.</summary>
    public static async Task ClearAsync(IJSRuntime js, string key)
    {
        try
        {
            await js.InvokeVoidAsync("nornisEditor.clearBackup", key);
        }
        catch (JSException)
        {
            // Nothing to remove, or nowhere to remove it from.
        }
    }

    /// <summary>
    /// How long ago the kept notes were written, in words. Relative on purpose: the page renders
    /// on the server, whose clock zone is not the reader's, so a clock time would be wrong for
    /// everyone who is not in the data centre.
    /// </summary>
    public static string DescribeAge(TimeSpan age)
    {
        if (age < TimeSpan.FromMinutes(1))
        {
            return "a moment ago";
        }

        if (age < TimeSpan.FromHours(1))
        {
            var minutes = (int)age.TotalMinutes;
            return minutes == 1 ? "a minute ago" : $"{minutes} minutes ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            var hours = (int)age.TotalHours;
            return hours == 1 ? "an hour ago" : $"{hours} hours ago";
        }

        var days = (int)age.TotalDays;
        return days == 1 ? "yesterday" : $"{days} days ago";
    }
}
