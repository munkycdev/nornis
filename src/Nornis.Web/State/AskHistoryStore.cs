using System.Text.Json;
using Microsoft.JSInterop;
using MudBlazor;
using Nornis.Web.Services;

namespace Nornis.Web.State;

/// <summary>
/// Reads and writes the per-world Ask history in browser localStorage — the one place that
/// touches it, because both Ask surfaces write and they used to do it two different ways.
///
/// <para>
/// The history was unbounded and every write swallowed its exception. localStorage is a few
/// megabytes per origin, so a heavy Ask user eventually hit quota and simply stopped keeping
/// history — no error, no notice, questions silently no longer saved. Now the list is capped,
/// a write that still will not fit sheds the oldest and retries, and the user is told once if
/// anything was actually dropped.
/// </para>
/// </summary>
public sealed class AskHistoryStore(IJSRuntime js, ISnackbar snackbar)
{
    /// <summary>
    /// How many conversations a world keeps. Ask history is a convenience, not a record — the
    /// canon lives in the world. Old threads are the cheapest thing to lose and the least
    /// missed, which is why the cap is on count and the newest survive.
    /// </summary>
    public const int MaxConversations = 50;

    /// <summary>How many times a quota failure sheds half and tries again before giving up.</summary>
    private const int ShedAttempts = 4;

    private bool _warned;

    public async Task<List<AskConversation>> LoadAsync(Guid? worldId)
    {
        try
        {
            var json = await js.InvokeAsync<string?>("localStorage.getItem", LoremasterDisplay.StorageKey(worldId));
            return string.IsNullOrWhiteSpace(json)
                ? []
                : JsonSerializer.Deserialize<List<AskConversation>>(json) ?? [];
        }
        catch
        {
            // Unreadable history is the same as none: the world's canon is untouched by this.
            return [];
        }
    }

    /// <summary>
    /// Persists <paramref name="conversations"/> for a world, newest first and capped. Takes the
    /// world explicitly because this is a read-modify-write with awaits in it, and which world's
    /// history this is must not be re-asked after one.
    /// </summary>
    public async Task SaveAsync(Guid worldId, List<AskConversation> conversations)
    {
        var key = LoremasterDisplay.StorageKey(worldId);
        var keep = conversations
            .OrderByDescending(c => c.UpdatedAt)
            .Take(MaxConversations)
            .ToList();
        var droppedByCap = conversations.Count - keep.Count;

        for (var attempt = 0; attempt <= ShedAttempts; attempt++)
        {
            try
            {
                await js.InvokeVoidAsync("localStorage.setItem", key, JsonSerializer.Serialize(keep));
                if (droppedByCap > 0 || attempt > 0)
                {
                    WarnOnce();
                }
                return;
            }
            catch (JSException)
            {
                // Quota, most likely — but any storage refusal is handled the same way, because
                // the browser does not reliably distinguish them and the remedy is identical.
                if (keep.Count <= 1)
                {
                    break;
                }

                droppedByCap += keep.Count - keep.Count / 2;
                keep = keep.Take(keep.Count / 2).ToList();
            }
            catch
            {
                // Circuit torn down mid-write, or JS unavailable. Nothing to say to a user who
                // is no longer there.
                return;
            }
        }

        WarnOnce();
    }

    /// <summary>
    /// Once per session. A user who has filled their storage will hit this on every question,
    /// and a snackbar per question is worse than the silence it replaced.
    /// </summary>
    private void WarnOnce()
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        snackbar.Add(
            $"Older Ask conversations were dropped — this browser keeps the {MaxConversations} most recent per world.",
            Severity.Info);
    }
}
