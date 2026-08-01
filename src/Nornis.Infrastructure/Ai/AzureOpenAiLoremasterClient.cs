using System.ClientModel;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nornis.Application.Ai;
using Nornis.Application.Configuration;
using OpenAI.Chat;

namespace Nornis.Infrastructure.Ai;

public class AzureOpenAiLoremasterClient : ILoremasterAiClient
{

    private readonly ChatClient _chatClient;
    private readonly LoremasterOptions _options;
    private readonly ILogger<AzureOpenAiLoremasterClient> _logger;

    public AzureOpenAiLoremasterClient(
        ChatClient chatClient,
        IOptions<LoremasterOptions> options,
        ILogger<AzureOpenAiLoremasterClient> logger)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<LoremasterAiResponse> AskAsync(AiPromptRequest request, CancellationToken ct)
    {
        // No MaxOutputTokenCount — see AzureOpenAiExtractionClient: the current SDK serialises
        // it as "max_tokens", which these deployments reject with HTTP 400.
        var (content, usage) = await AzureOpenAiCallExecutor.ExecuteAsync(
            _chatClient, request, new ChatCompletionOptions(), "Loremaster", _logger, ct);

        return new LoremasterAiResponse
        {
            AnswerText = content,
            Usage = usage
        };
    }
}
