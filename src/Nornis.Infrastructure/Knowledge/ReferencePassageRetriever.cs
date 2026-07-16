using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using Nornis.Application.Knowledge;
using Nornis.Application.Services;
using Nornis.Domain.Entities;
using Nornis.Domain.Enums;
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

    public async Task<IReadOnlyList<KnowledgePassage>> RetrieveAsync(
        string question,
        Guid worldId,
        Guid userId,
        WorldRole role,
        CancellationToken ct)
    {
        var allowedScopes = LibraryService.GetAllowedScopes(role);

        if (!await _documentRepository.AnyIndexedAsync(worldId, allowedScopes, ct))
        {
            return [];
        }

        try
        {
            var embedding = await _embeddingClient.EmbedAsync([question], ct);
            await TrackUsageAsync(worldId, userId, embedding.InputTokens, ct);

            var hits = await _chunkRepository.SearchAsync(
                worldId, embedding.Embeddings[0], allowedScopes, _options.RetrievalTopK, ct);

            return hits.Select(h => new KnowledgePassage
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
            // Reference passages enrich an answer; their failure must never sink the ask.
            _logger.LogError(ex, "Reference passage retrieval failed for world {WorldId}", worldId);
            return [];
        }
    }

    private async Task TrackUsageAsync(Guid worldId, Guid userId, int inputTokens, CancellationToken ct)
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
