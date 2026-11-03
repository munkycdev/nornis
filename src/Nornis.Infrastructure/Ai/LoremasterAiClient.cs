using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Nornis.Application.Ai;

namespace Nornis.Infrastructure.Ai;

public class LoremasterAiClient : ILoremasterAiClient
{
    private readonly IAiCallBinder _binder;
    private readonly ILogger<LoremasterAiClient> _logger;

    public LoremasterAiClient(IAiCallBinder binder, ILogger<LoremasterAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    // No output ceiling or temperature — see UnsupportedChatParameterTests.
    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct) =>
        AiCallExecutor.StreamAsync(_binder, AiTask.Ask, request, options: null, "Loremaster", _logger, ct);
}
