namespace Nornis.Application.Configuration;

public class LoremasterOptions
{
    public string AiModel { get; set; } = string.Empty;
    public string AiEndpoint { get; set; } = string.Empty;
    public int AiTimeoutSeconds { get; set; } = 30;
    public int MaxRetrievalCount { get; set; } = 30;
    public int MaxFactsPerArtifact { get; set; } = 15;
    public int MaxContextTokens { get; set; } = 8000;
    public int MaxQuestionLength { get; set; } = 2000;
    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new();
}
