namespace Nornis.Application.Configuration;

public class ExtractionOptions
{
    public const string SectionName = "Extraction";

    public string AiModel { get; set; } = string.Empty;
    public string AiEndpoint { get; set; } = string.Empty;
    public int AiTimeoutSeconds { get; set; } = 60;
    public int MaxArtifactContextCount { get; set; } = 50;
    public int MaxFactsPerArtifact { get; set; } = 20;
    public int MaxParseRetryAttempts { get; set; } = 2;

    /// <summary>Map extraction's second pass: re-read positions per cropped tile for
    /// precision. A few extra vision calls per map; disable to fall back to one pass.</summary>
    public bool MapRefinePositions { get; set; } = true;

    /// <summary>How many prior timeline sources to walk back looking for the party's last
    /// known location before giving up; the nearest one with accepted Location links wins.
    /// Zero disables location context entirely.</summary>
    public int LocationContextLookbackSources { get; set; } = 5;
    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new();
}
