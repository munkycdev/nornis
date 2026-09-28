using System.Text;
using Nornis.Web.ApiClient;

namespace Nornis.Web.Services;

/// <summary>
/// A Loremaster answer while it is being written, for the Ask surfaces to render as it grows.
/// The members' Ask page, the rail panel and the public world page all read their stream through
/// this, so what counts as finished, when to re-render and how a reader leaving is handled are
/// decided once.
/// </summary>
public sealed class StreamingAnswer
{
    /// <summary>
    /// Deltas arrive many times a second, and every render ships the whole answer so far over the
    /// circuit — rendering each one would send a two-thousand-character answer a few hundred times.
    /// A handful of renders a second reads as smooth; the first words and the end render at once.
    /// </summary>
    internal static readonly TimeSpan RenderInterval = TimeSpan.FromMilliseconds(80);

    private readonly StringBuilder _text = new();
    private readonly TimeProvider _time;
    private long _lastRender;

    public StreamingAnswer(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Known before the first word: it is decided by what retrieval found.</summary>
    public string? Confidence { get; private set; }

    public IReadOnlyList<string> Caveats { get; private set; } = [];

    /// <summary>The text so far, citation markers removed — including one still half-written.</summary>
    public string Text => LoremasterDisplay.CleanAnswer(_text.ToString());

    public bool HasText => _text.Length > 0;

    /// <summary>The whole answer, once the stream says it is done.</summary>
    public AskAnswer? Answer { get; private set; }

    /// <summary>Why there is no answer. A partial one is not kept: text that stopped midway reads
    /// as whole, and the Loremaster's promise is to say where the record runs out.</summary>
    public ApiError? Error { get; private set; }

    /// <summary>
    /// Reads the stream to its end, calling <paramref name="render"/> as the answer grows.
    /// </summary>
    /// <param name="stillWanted">Checked before each event. A world switch mid-answer makes the
    /// answer belong to nothing on screen; returning false stops reading, which aborts the request
    /// and the model call behind it rather than paying for words nobody will see.</param>
    /// <returns>False when reading stopped early — unwanted, or the component's token cancelled —
    /// and the caller should do nothing with the result.</returns>
    public async Task<bool> ReadAsync(
        IAsyncEnumerable<AskStreamEvent> events, Func<bool> stillWanted, Action render)
    {
        try
        {
            await foreach (var e in events)
            {
                if (!stillWanted())
                {
                    return false;
                }

                if (Apply(e))
                {
                    render();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The component went away mid-answer and cancelled its token. Nothing to show it to.
            return false;
        }

        return Answer is not null || Error is not null;
    }

    /// <summary>Applies one event; true when the change is worth rendering now.</summary>
    internal bool Apply(AskStreamEvent e)
    {
        switch (e)
        {
            case AskStreamEvent.Started started:
                Confidence = started.Confidence;
                Caveats = started.Caveats;
                return true;

            case AskStreamEvent.Delta delta:
                var first = _text.Length == 0;
                _text.Append(delta.Text);
                var now = _time.GetTimestamp();
                if (!first && _time.GetElapsedTime(_lastRender, now) < RenderInterval)
                {
                    return false;
                }

                _lastRender = now;
                return true;

            case AskStreamEvent.Done done:
                Answer = done.Answer;
                return true;

            case AskStreamEvent.Failed failed:
                Error = failed.Error;
                _text.Clear();
                return true;

            default:
                return false;
        }
    }
}
