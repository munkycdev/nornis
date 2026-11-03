using Nornis.Application.Ai;
using Nornis.Domain.Enums;

namespace Nornis.Application.Services;

/// <summary>
/// Feature 39, Phase A: every call runs on Nornis's own Azure OpenAI key, exactly as before user
/// keys existed. Phase B replaces this with the real funding rule (user keys, then the trial).
/// </summary>
public sealed class NornisAiFundingService : IAiFundingService
{
    public Task<AiFunding> ResolveAsync(AiCallScope scope, AiTask task, string nornisModel, CancellationToken ct) =>
        Task.FromResult(new AiFunding(AiProvider.AzureOpenAi, nornisModel, AiFundedBy.Nornis));
}
