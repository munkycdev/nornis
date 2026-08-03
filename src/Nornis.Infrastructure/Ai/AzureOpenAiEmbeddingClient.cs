using System.ClientModel;
using System.Net;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using OpenAI.Embeddings;

namespace Nornis.Infrastructure.Ai;

/// <summary>Azure OpenAI embeddings via the shared account's nornis-embed deployment.</summary>
public sealed class AzureOpenAiEmbeddingClient : IEmbeddingClient
{
    private readonly EmbeddingClient _client;
    private readonly LibraryOptions _options;

    public AzureOpenAiEmbeddingClient(EmbeddingClient client, IOptions<LibraryOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken ct)
    {
        // The linked-CTS shape the nine chat clients get from AzureOpenAiCallExecutor. Linked
        // rather than standalone so a caller cancelling still cancels the call, and so the catch
        // below can tell "we gave up waiting" from "the caller left".
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AiTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        ClientResult<OpenAIEmbeddingCollection> response;
        try
        {
            response = await _client.GenerateEmbeddingsAsync(inputs, cancellationToken: linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            // AiTimeoutException rather than the raw cancellation, because TransientFailureClassifier
            // reads the two oppositely: a timeout is transient and retries, an OperationCanceledException
            // is the caller's decision and does not. Letting the raw one out would permanently fail a
            // library document for being slow.
            throw new AiTimeoutException(
                $"Embedding call timed out after {_options.AiTimeoutSeconds} seconds.");
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
