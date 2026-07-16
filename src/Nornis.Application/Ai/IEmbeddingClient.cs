namespace Nornis.Application.Ai;

public sealed record EmbeddingResult(IReadOnlyList<float[]> Embeddings, int InputTokens);

/// <summary>Turns text into embedding vectors (library chunks at index time, the question at ask time).</summary>
public interface IEmbeddingClient
{
    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct);
}
