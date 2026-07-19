using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
using Nornis.Domain.Models;
using Nornis.Domain.Repositories;

namespace Nornis.Infrastructure.Knowledge;

/// <summary>
/// Vector retrieval over indexed library documents: embed the question, nearest-chunk
/// search in SQL. Skips the embedding call entirely when the world has no indexed
/// documents in the caller's scopes, so ordinary asks pay nothing.
/// </summary>
public class ReferencePassageRetriever : IReferencePassageRetriever
{
    private readonly ILibraryDocumentRepository _documentRepository;
    private readonly ILibraryChunkRepository _chunkRepository;
    private readonly IEmbeddingClient _embeddingClient;
    private readonly IAiUsageRecordRepository _usageRepository;
    private readonly LibraryOptions _options;
    private readonly ILogger<ReferencePassageRetriever> _logger;

    public ReferencePassageRetriever(
        ILibraryDocumentRepository documentRepository,
        ILibraryChunkRepository chunkRepository,
        IEmbeddingClient embeddingClient,
        IAiUsageRecordRepository usageRepository,
        IOptions<LibraryOptions> options,
        ILogger<ReferencePassageRetriever> logger)
    {
        _documentRepository = documentRepository;
        _chunkRepository = chunkRepository;
        _embeddingClient = embeddingClient;
        _usageRepository = usageRepository;
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(
        string question,
        Guid worldId,
        Guid userId,
        WorldRole role,
        CancellationToken ct)
        => RetrieveCoreAsync(question, worldId, LibraryService.GetAllowedScopes(role), userId, ct);

    public Task<IReadOnlyList<KnowledgePassage>> RetrieveForScopesAsync(
        string query,
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedScopes,
        Guid? attributedUserId,
        CancellationToken ct)
        => RetrieveCoreAsync(query, worldId, allowedScopes, attributedUserId, ct);

    private async Task<IReadOnlyList<KnowledgePassage>> RetrieveCoreAsync(
        string query,
        Guid worldId,
        IReadOnlyList<VisibilityScope> allowedScopes,
        Guid? attributedUserId,
        CancellationToken ct)
    {
        if (allowedScopes.Count == 0 || !await _documentRepository.AnyIndexedAsync(worldId, allowedScopes, ct))
        {
            return [];
        }

        try
        {
            var embedding = await _embeddingClient.EmbedAsync([query], ct);
            await TrackUsageAsync(worldId, attributedUserId, embedding.InputTokens, ct);

            var hits = await _chunkRepository.SearchAsync(
                worldId, embedding.Embeddings[0], allowedScopes, _options.RetrievalTopK, ct);

            var expanded = await ExpandWithNeighborsAsync(hits, ct);

            return expanded.Select(h => new KnowledgePassage
            {
                ChunkId = h.ChunkId,
                DocumentId = h.DocumentId,
                DocumentTitle = h.DocumentTitle,
                Page = h.Page,
                Text = h.Text,
                ReferenceId = $"passage:{h.ChunkId}",
            }).ToList();
        }
        catch (Exception ex)
        {
            // Reference passages enrich the result; their failure must never sink the primary op.
            _logger.LogError(ex, "Reference passage retrieval failed for world {WorldId}", worldId);
            return [];
        }
    }

    /// <summary>
    /// Pulls each seed hit's adjacent chunks (±NeighborRadius within its document) so
    /// content split across a chunk boundary reads whole, then orders everything by
    /// document reading order. Seeds always survive the cap; neighbors fill the rest.
    /// </summary>
    private async Task<IReadOnlyList<LibraryChunkHit>> ExpandWithNeighborsAsync(
        IReadOnlyList<LibraryChunkHit> hits, CancellationToken ct)
    {
        if (hits.Count == 0 || _options.NeighborRadius <= 0)
        {
            return hits;
        }

        var byId = hits.ToDictionary(h => h.ChunkId);
        var neighbors = new List<LibraryChunkHit>();

        foreach (var documentGroup in hits.GroupBy(h => h.DocumentId))
        {
            var wantedOrds = documentGroup
                .SelectMany(h => Enumerable.Range(h.Ord - _options.NeighborRadius, _options.NeighborRadius * 2 + 1))
                .Where(o => o >= 0)
                .Except(documentGroup.Select(h => h.Ord))
                .Distinct()
                .ToList();

            if (wantedOrds.Count > 0)
            {
                neighbors.AddRange(await _chunkRepository.ListByDocumentOrdsAsync(documentGroup.Key, wantedOrds, ct));
            }
        }

        var extras = neighbors
            .Where(n => !byId.ContainsKey(n.ChunkId))
            .Take(Math.Max(0, _options.MaxContextPassages - hits.Count));

        return hits.Concat(extras)
            .OrderBy(h => h.DocumentTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(h => h.Ord)
            .ToList();
    }

    private async Task TrackUsageAsync(Guid worldId, Guid? userId, int inputTokens, CancellationToken ct)
    {
        try
        {
            var pricing = _options.ModelPricing.GetValueOrDefault(_options.EmbeddingDeployment);
            await _usageRepository.CreateAsync(new AiUsageRecord
            {
                Id = Guid.NewGuid(),
                WorldId = worldId,
                UserId = userId,
                OperationType = AiOperationType.Embedding,
                Model = _options.EmbeddingDeployment,
                InputTokens = inputTokens,
                OutputTokens = 0,
                TotalTokens = inputTokens,
                EstimatedCostUsd = pricing is null ? 0m : inputTokens * pricing.InputPerMillionTokensUsd / 1_000_000m,
                DurationMs = 0,
                Succeeded = true,
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record question-embedding usage for world {WorldId}", worldId);
        }
    }
}
