namespace Nornis.Application.Ai;

/// <summary>
/// The usage block every AI response carries — one value instead of five properties
/// re-declared per response record. The model rides with the numbers because cost
/// lookup keys on the model the response <em>reports</em>, not the one configured: a
/// deployment alias can serve a different model than requested, and the ledger must
/// price what actually ran.
/// </summary>
public sealed record AiUsage
{
    public required string Model { get; init; }

    public required int InputTokens { get; init; }

    public required int OutputTokens { get; init; }

    public required int TotalTokens { get; init; }

    public required int DurationMs { get; init; }

    /// <summary>
    /// Portion of <see cref="InputTokens"/> the provider served from its prompt cache,
    /// when it reports one. Null means the provider said nothing — which is not the
    /// same as zero: "no cache hit" and "we cannot see cache hits" must stay
    /// distinguishable when judging whether prompt-cache work paid off.
    /// </summary>
    public int? CachedInputTokens { get; init; }
}
