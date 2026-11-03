namespace Nornis.Domain.Enums;

/// <summary>
/// Who serves an AI call (feature 39). <see cref="AzureOpenAi"/> is Nornis's own resource, which
/// pays for the trial and for embeddings; the others are reached with a user's own key.
/// </summary>
public enum AiProvider
{
    AzureOpenAi,
    OpenAi,
    Anthropic,
    Gemini,
    OpenAiCompatible
}
