using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

/// <summary>
/// Prep's drafting transport, on the Loremaster's chat client unless <c>Prep:Deployment</c>
/// names another deployment. The only client allowed an output ceiling: the 2026-07-27 outage
/// was a ceiling sent as <c>max_tokens</c>, and this one goes out as
/// <c>max_completion_tokens</c>, which <c>OutputCeilingWireTests</c> pins and
/// <c>LiveOutputCeilingTests</c> proved the deployment accepts (feature 29, task A2).
/// </summary>
public class AzureOpenAiAdventureDraftingClient : IAdventureDraftingAiClient
{
    /// <summary>
    /// The prompt asks for at most about 1,200 words, roughly 1,600 tokens, so this is headroom
    /// rather than the expected length. Reasoning tokens count against it on this model family.
    /// </summary>
    public const int OutputCeilingTokens = 2_500;

    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiAdventureDraftingClient> _logger;

    public AzureOpenAiAdventureDraftingClient(IChatClient chatClient, ILogger<AzureOpenAiAdventureDraftingClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    /// <summary>
    /// The ceiling is set on the SDK's own options through the factory, the shape the wire test
    /// reads. A factory, not a shared instance, because the SDK mutates what it is given.
    /// </summary>
    public static ChatOptions BuildChatOptions() => new()
    {
        RawRepresentationFactory = _ => new OpenAI.Chat.ChatCompletionOptions { MaxOutputTokenCount = OutputCeilingTokens },
    };

    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct) =>
        AzureOpenAiCallExecutor.StreamAsync(_chatClient, request, BuildChatOptions(), "AdventureDrafting", _logger, ct);
}
