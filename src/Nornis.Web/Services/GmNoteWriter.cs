using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// Turns a GM's note into a source and queues it for extraction.
/// </summary>
/// <remarks>
/// A GM note is not a new kind of record — it is an ordinary <c>GMNote</c> source, created and
/// then marked ready so the worker reads it and proposes updates through the normal review
/// queue. That is what lets a note change canon: the GM writes "the key was never in his
/// quarters", extraction proposes the correction, and the GM accepts it.
/// <para>
/// The sequence lives here rather than in the dialog so it can be tested without rendering one.
/// </para>
/// </remarks>
public class GmNoteWriter
{
    private readonly NornisApiClient _api;

    public GmNoteWriter(NornisApiClient api) => _api = api;

    /// <summary>
    /// Creates the note and queues it. Returns the new source's id, or the message to show the
    /// GM — including the partial-failure case where the note was saved but never queued.
    /// </summary>
    /// <param name="subject">
    /// What the GM was reading, if the page knew. Prefixed onto the body so extraction can match
    /// the note to that artifact by name.
    /// </param>
    public async Task<ApiResult<Guid>> SaveAsync(
        Guid worldId,
        string? subject,
        string text,
        string visibility,
        CancellationToken ct = default)
    {
        var created = await _api.CreateSourceAsync(worldId, BuildRequest(subject, text, visibility), ct);
        if (!created.IsSuccess)
        {
            return ApiResult<Guid>.Fail(created.Error!);
        }

        var sourceId = created.Value!.Id;
        var ready = await _api.MarkSourceReadyAsync(worldId, sourceId, ct);
        if (!ready.IsSuccess)
        {
            // The note survives as a draft, so nothing the GM typed is lost — but it will sit
            // unread until someone queues it from the sources ledger. Say so rather than
            // reporting a plain failure.
            return ApiResult<Guid>.Fail(new ApiError(
                ready.Error!.Code,
                $"Saved as a draft, but could not queue it for reading: {ready.Error!.Message}"));
        }

        return ApiResult<Guid>.Ok(sourceId);
    }

    internal static CreateSourceRequest BuildRequest(string? subject, string text, string visibility)
    {
        var trimmed = text.Trim();
        var named = !string.IsNullOrWhiteSpace(subject);

        return new CreateSourceRequest(
            Title: named
                ? $"Note on {subject} — {DateTime.Now:yyyy-MM-dd}"
                : $"GM note — {DateTime.Now:yyyy-MM-dd}",
            Type: "GMNote",
            Visibility: visibility,
            Body: named ? $"Regarding {subject}: {trimmed}" : trimmed,
            Uri: null,
            OccurredAt: null);
    }
}
