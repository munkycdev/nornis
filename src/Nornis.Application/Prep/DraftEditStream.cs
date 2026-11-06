using System.Text;

namespace Nornis.Application.Prep;

/// <summary>
/// What a Design turn shows while its answer streams (feature 41): the prose, and never the text
/// of an edit fence, which goes into the draft rather than the chat. Display only: the saved answer
/// is still read by <see cref="DraftEdits"/> once it ends, so a fence hidden here that turns out
/// not to follow the shape shows up as prose when the conversation reloads.
///
/// Works on whole lines, since a fence's markers are lines: text after the last newline waits for
/// the next piece, or for <see cref="Flush"/>.
/// </summary>
public sealed class DraftEditStream
{
    private readonly StringBuilder _pending = new();

    /// <summary>The open fence's backticks; null outside a fence.</summary>
    private string? _ticks;

    /// <summary>The line before was the end marker, so the fence's own backticks close it now.</summary>
    private bool _afterEnd;

    /// <summary>
    /// How much of the unfinished last line has already gone out as prose: a line that can't open
    /// a fence (it doesn't start with a backtick) streams as it comes rather than waiting for its end.
    /// </summary>
    private int _shownOfPending;

    /// <summary>The prose this piece adds, and how many edit fences it opened.</summary>
    public (string Prose, int EditsOpened) Push(string text)
    {
        _pending.Append(text);
        var buffered = _pending.ToString();
        var lastNewline = buffered.LastIndexOf('\n');
        var prose = new StringBuilder();
        var opened = 0;
        if (lastNewline >= 0)
        {
            var (complete, count) = Read(buffered[..(lastNewline + 1)], _shownOfPending);
            prose.Append(complete);
            opened = count;
            _shownOfPending = 0;
            _pending.Clear().Append(buffered.AsSpan(lastNewline + 1));
        }

        var partial = _pending.ToString();
        if (_ticks is null && partial.TrimStart().Length > 0 && !partial.TrimStart().StartsWith('`'))
        {
            prose.Append(partial.AsSpan(_shownOfPending));
            _shownOfPending = partial.Length;
        }

        return (prose.ToString(), opened);
    }

    /// <summary>What is left once the stream has ended: a last line with no newline after it.</summary>
    public (string Prose, int EditsOpened) Flush()
    {
        var rest = _pending.ToString();
        var shown = _shownOfPending;
        _pending.Clear();
        _shownOfPending = 0;
        return rest.Length == 0 ? ("", 0) : Read(rest, shown);
    }

    /// <summary>Reads whole lines; the first <paramref name="alreadyShown"/> characters of the first went out already.</summary>
    private (string Prose, int EditsOpened) Read(string lines, int alreadyShown)
    {
        var prose = new StringBuilder();
        var opened = 0;
        var first = true;
        foreach (var whole in SplitKeepingEnds(lines))
        {
            var line = first ? whole[Math.Min(alreadyShown, whole.Length)..] : whole;
            first = false;
            var trimmed = whole.Trim();
            if (_ticks is null)
            {
                if (DraftEdits.OpeningTicks(trimmed) is { } ticks)
                {
                    _ticks = ticks;
                    _afterEnd = false;
                    opened++;
                }
                else
                {
                    prose.Append(line);
                }
            }
            else
            {
                // Three backticks also close a stat block inside the edit, so only the line after
                // the end marker can close a three-backtick fence; four or more can't be mistaken.
                if (trimmed == _ticks && (_afterEnd || _ticks.Length > 3))
                {
                    _ticks = null;
                }

                _afterEnd = trimmed == DraftEdits.EndMarker;
            }
        }

        return (prose.ToString(), opened);
    }

    private static IEnumerable<string> SplitKeepingEnds(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                yield return text[start..(i + 1)];
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            yield return text[start..];
        }
    }
}
