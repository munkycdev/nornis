using System.Text.Json;

namespace Nornis.Web.Services;

/// <summary>
/// The form half of the capture page's safety net: the fields around the notes, kept in the
/// browser's localStorage so a mis-click on the sidebar does not cost a title, a date and a
/// campaign along with the text. The notes themselves are kept by nornis-editor.js under the
/// sibling key; both are forgotten when a save succeeds or the GM starts fresh.
///
/// Not a Draft. A Draft is a saved <c>Source</c> that has not been sent for extraction; this is
/// the one that was never saved at all.
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

public static class CaptureBackup
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Where the editor keeps the notes for a world. One per world, per browser.</summary>
    public static string NotesKey(Guid worldId) => $"nornis:capture:{worldId}:notes";

    /// <summary>Where the page keeps the form for a world.</summary>
    public static string FormKey(Guid worldId) => $"nornis:capture:{worldId}:form";

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
