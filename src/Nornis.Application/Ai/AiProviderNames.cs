using Nornis.Domain.Enums;

namespace Nornis.Application.Ai;

/// <summary>How a provider is named to a person (feature 39).</summary>
public static class AiProviderNames
{
    public static string Display(AiProvider provider) => provider switch
    {
        AiProvider.AzureOpenAi => "Azure OpenAI",
        AiProvider.OpenAi => "OpenAI",
        AiProvider.Anthropic => "Anthropic",
        AiProvider.Gemini => "Google Gemini",
        AiProvider.OpenAiCompatible => "OpenAI-compatible",
        _ => provider.ToString(),
    };
}
