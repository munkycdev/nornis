using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiLoremasterClient : ILoremasterAiClient
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<AzureOpenAiLoremasterClient> _logger;

    public AzureOpenAiLoremasterClient(IChatClient chatClient, ILogger<AzureOpenAiLoremasterClient> logger)
    {
        _chatClient = chatClient;
        _logger = logger;
    }

    // No output ceiling or temperature — see UnsupportedChatParameterTests.
    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct) =>
        AzureOpenAiCallExecutor.StreamAsync(_chatClient, request, chatOptions: null, "Loremaster", _logger, ct);
}
