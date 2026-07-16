namespace Nornis.Application.Configuration;

/// <summary>
/// Library indexing + retrieval settings. The embedding client reuses each host's existing
/// Azure OpenAI endpoint/key (Loremaster in the API, Extraction in the worker) — only the
/// deployment name is Library-specific.
/// </summary>
public class LibraryOptions
{
    public const string SectionName = "Library";

    /// <summary>Azure OpenAI embedding deployment name. Must match a ModelPricing key
    /// or embedding cost silently records $0.</summary>
    public string EmbeddingDeployment { get; set; } = "nornis-embed";

    /// <summary>Embedding vector width — must equal the chunk column's vector dimensions.</summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>Target chunk size in characters (~800 tokens).</summary>
    public int MaxChunkChars { get; set; } = 3200;

    /// <summary>Characters of trailing overlap carried into the next chunk.</summary>
    public int OverlapChars { get; set; } = 480;

    /// <summary>Chunks per embedding API call.</summary>
    public int EmbedBatchSize { get; set; } = 64;

    /// <summary>Passages retrieved per ask.</summary>
    public int RetrievalTopK { get; set; } = 6;

    public int MaxUploadSizeBytes { get; set; } = 209_715_200; // 200 MB, matches Chronicis

    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new()
    {
        ["nornis-embed"] = new ModelPricing
        {
            InputPerMillionTokensUsd = 0.02m,
            OutputPerMillionTokensUsd = 0m,
        },
    };
}
