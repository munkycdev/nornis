using System.Runtime.CompilerServices;
using Nornis.Application.Ai;

namespace Nornis.Application.Tests.Fakes;

/// <summary>
/// A whole Loremaster answer as a test wants to state it — the text and what the call cost — and
/// the stream a model would have delivered it as. Tests reason about answers; the service reads
/// chunks. This is the one place the two are translated.
/// </summary>
public sealed record CannedLoremasterResponse
{
    public required string AnswerText { get; init; }

    public required AiUsage Usage { get; init; }

    /// <summary>
    /// The answer in a few pieces, split without regard to <c>[ref:…]</c> markers exactly as a
    /// model's token stream is, then the final usage chunk.
    /// </summary>
    public IAsyncEnumerable<AiStreamChunk> AsStream(CancellationToken ct = default) =>
        StreamChunks(SplitIntoChunks(AnswerText), Usage, ct);

    /// <summary>Splits text into up to three contiguous, non-empty pieces.</summary>
    public static IReadOnlyList<string> SplitIntoChunks(string text, int pieces = 3)
    {
        if (text.Length == 0)
        {
            return [];
        }

        var size = (int)Math.Ceiling(text.Length / (double)pieces);
        var chunks = new List<string>();
        for (var start = 0; start < text.Length; start += size)
        {
            chunks.Add(text.Substring(start, Math.Min(size, text.Length - start)));
        }

        return chunks;
    }

    /// <summary>
    /// Yields each chunk as text, then — when <paramref name="usage"/> is given — the empty-text
    /// usage chunk a completed stream ends with. Null leaves it out, as a stream cut short does.
    /// </summary>
    public static async IAsyncEnumerable<AiStreamChunk> StreamChunks(
        IEnumerable<string> chunks,
        AiUsage? usage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new AiStreamChunk { Text = chunk };
        }

        if (usage is not null)
        {
            yield return new AiStreamChunk { Text = string.Empty, Usage = usage };
        }
    }
}
