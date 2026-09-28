using Nornis.Application.Errors;

namespace Nornis.Application.Models;

/// <summary>
/// An answer that has been grounded and priced but not yet written. Everything that can refuse
/// the question — validation, the budget, retrieval — has already run by the time one of these
/// exists, so a caller can commit to a response before the first word arrives.
/// </summary>
public sealed class LoremasterAnswerStream
{
    /// <summary>Decided by what retrieval found, so it is known before the model starts.</summary>
    public required ConfidenceLevel Confidence { get; init; }

    public required IReadOnlyList<string> Caveats { get; init; }

    /// <summary>
    /// Text deltas, then exactly one <see cref="LoremasterStreamEvent.Completed"/> or
    /// <see cref="LoremasterStreamEvent.Failed"/>. Enumerate it once: each enumeration is a paid
    /// model call, and the usage ledger is written as it ends.
    /// </summary>
    public required IAsyncEnumerable<LoremasterStreamEvent> Events { get; init; }
}

public abstract record LoremasterStreamEvent
{
    private LoremasterStreamEvent() { }

    /// <summary>The next piece of the answer text, [ref:…] markers included.</summary>
    public sealed record Delta(string Text) : LoremasterStreamEvent;

    public sealed record Completed(LoremasterAnswer Answer) : LoremasterStreamEvent;

    /// <summary>
    /// The model call failed. Text already delivered stays delivered; the reader is told the
    /// answer is incomplete rather than left to take a truncated one as whole.
    /// </summary>
    public sealed record Failed(AppError Error) : LoremasterStreamEvent;
}
