namespace Nornis.Application.Configuration;

public class ExtractionOptions
{
    public string AiModel { get; set; } = string.Empty;
    public string AiEndpoint { get; set; } = string.Empty;
    public int AiTimeoutSeconds { get; set; } = 60;
    public int MaxArtifactContextCount { get; set; } = 50;
    public int MaxFactsPerArtifact { get; set; } = 20;
    public int MaxParseRetryAttempts { get; set; } = 2;
    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new();
}
