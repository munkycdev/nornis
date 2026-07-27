namespace Nornis.Application.Ai;

public class AiExtractionResponse
{
    public required IReadOnlyList<ExtractionProposal> Proposals { get; init; }
    public required int InputTokens { get; init; }

    /// <summary>
    /// Portion of <see cref="InputTokens"/> the provider served from its prompt cache, when it
    /// reports one. Null means the provider said nothing — which is not the same as zero, and
    /// the distinction matters: "no cache hit" and "we cannot see cache hits" would otherwise be
    /// indistinguishable when judging whether prompt-cache work paid off.
    /// </summary>
    public int? CachedInputTokens { get; init; }

    public required int OutputTokens { get; init; }
    public required int TotalTokens { get; init; }
    public required int DurationMs { get; init; }
    public required string Model { get; init; }
}
