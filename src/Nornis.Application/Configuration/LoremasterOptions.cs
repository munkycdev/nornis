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

    /// <summary>
    /// Per-session character cap for the recent-session block. Session bodies are validated up
    /// to 100,000 characters, and <see cref="RecentSessionCount"/> of them ride along on every
    /// ask — including anonymous public ones — so uncapped this single block could dwarf the
    /// rest of the prompt many times over. Truncation prefers the head of the note, where
    /// recaps and summaries are conventionally written.
    /// </summary>
    public int MaxSessionChars { get; set; } = 4000;

    /// <summary>
    /// Ceiling on the assembled knowledge context, in estimated tokens. Sections are appended
    /// in descending value and appending stops once this is reached, so the first things
    /// dropped are source-reference quotes and library passages.
    /// </summary>
    public int MaxContextTokens { get; set; } = 8000;
    public int MaxQuestionLength { get; set; } = 2000;
    public Dictionary<string, ModelPricing> ModelPricing { get; set; } = new();
}
