namespace Nornis.Application.Configuration;

public class LoremasterOptions
{
    public string AiModel { get; set; } = string.Empty;
    public string AiEndpoint { get; set; } = string.Empty;
    public int AiTimeoutSeconds { get; set; } = 30;
    public int MaxRetrievalCount { get; set; } = 30;
    public int MaxFactsPerArtifact { get; set; } = 15;

    /// <summary>How many of the world's most recent sessions ride along in the Ask
    /// context to ground time-anchored questions ("what happened last session?").</summary>
    public int RecentSessionCount { get; set; } = 3;
    public int MaxContextTokens { get; set; } = 8000;
    public int MaxQuestionLength { get; set; } = 2000;
    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new();
}
