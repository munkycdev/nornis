using Nornis.Application.Ai;
using OpenAI.Embeddings;

namespace Nornis.Infrastructure.Ai;

/// <summary>Azure OpenAI embeddings via the shared account's nornis-embed deployment.</summary>
public sealed class AzureOpenAiEmbeddingClient : IEmbeddingClient
{
    private readonly EmbeddingClient _client;

    public AzureOpenAiEmbeddingClient(EmbeddingClient client)
    {
        _client = client;
    }

    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        var response = await _client.GenerateEmbeddingsAsync(inputs, cancellationToken: ct);
        var collection = response.Value;

        var embeddings = collection
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        return new EmbeddingResult(embeddings, collection.Usage?.InputTokenCount ?? 0);
    }
}
