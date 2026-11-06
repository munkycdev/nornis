namespace Nornis.Infrastructure.Ai;

/// <summary>Which of a provider's APIs a user-key client speaks (feature 40).</summary>
public enum AiClientShape
{
    /// <summary>The provider's chat API: Chat Completions on the OpenAI wire. Every call but a search.</summary>
    Chat,

    /// <summary>
    /// OpenAI's Responses API, built only for a call that searches the web: OpenAI offers its search
    /// tool there and not on Chat Completions. Refused for any other provider, whose chat API
    /// already takes its search tool.
    /// </summary>
    Responses,
}
