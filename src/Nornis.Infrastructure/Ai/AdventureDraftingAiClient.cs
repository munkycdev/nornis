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
public class AdventureDraftingAiClient : IAdventureDraftingAiClient
{
    /// <summary>
    /// The prompt asks for at most about 1,200 words, roughly 1,600 tokens, so this is headroom
    /// rather than the expected length. Reasoning tokens count against it on this model family.
    /// </summary>
    public const int OutputCeilingTokens = 2_500;

    private readonly IAiCallBinder _binder;
    private readonly ILogger<AdventureDraftingAiClient> _logger;

    public AdventureDraftingAiClient(IAiCallBinder binder, ILogger<AdventureDraftingAiClient> logger)
    {
        _binder = binder;
        _logger = logger;
    }

    /// <summary>The ceiling, in provider-neutral terms; each provider's traits put it on the wire
    /// its own way (OpenAI's as <c>max_completion_tokens</c>, which <c>OutputCeilingWireTests</c> reads).</summary>
    public static AiCallOptions CallOptions { get; } = new(MaxOutputTokens: OutputCeilingTokens);

    public IAsyncEnumerable<AiStreamChunk> StreamAsync(AiPromptRequest request, CancellationToken ct) =>
        AiCallExecutor.StreamAsync(_binder, AiTask.Prep, request, CallOptions, "AdventureDrafting", _logger, ct);
}
