using System.ClientModel;
using System.Net;
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
        ClientResult<OpenAIEmbeddingCollection> response;
        try
        {
            response = await _client.GenerateEmbeddingsAsync(inputs, cancellationToken: ct);
        }
        catch (ClientResultException ex)
        {
            // Translate at the boundary, exactly as the chat clients do. Without this the raw SDK
            // exception reaches the application layer, which cannot read its status without
            // depending on the SDK — so a genuine 429 was being classified by matching text, and
            // a throttled embedding could permanently fail a library document instead of retrying.
            throw new HttpRequestException(
                $"Embedding call failed: HTTP {ex.Status}",
                ex,
                (HttpStatusCode)ex.Status);
        }

        var collection = response.Value;

        var embeddings = collection
            .OrderBy(e => e.Index)
            .Select(e => e.ToFloats().ToArray())
            .ToList();

        return new EmbeddingResult(embeddings, collection.Usage?.InputTokenCount ?? 0);
    }
}
